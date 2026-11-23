import {aggregate,deduplicate,filterReports} from './model.mjs';
import {indexLiveStreams,projectLiveRuns} from './telemetry.mjs';

const sameKey = (next, previous) => previous!==null && next.every((value,index)=>value===previous[index]);

// Call the setters for each incoming dataset, including updates to a reused array.
// Read results are shared with the views and must be treated as read-only.
export function createDashboardData() {
  let history=deduplicate([]), telemetry=indexLiveStreams([]), documents=0;
  let reportSource, reportKey=null, reports;
  let totalSource, totalBots, totals;
  let streamSource, streamKey=null, streams;

  function setHistory(records) {
    const parsed=deduplicate(records);
    // A new report identity invalidates the metric index when an incoming row is reused.
    history={...parsed,reports:parsed.reports.map(report=>({...report}))};
    documents=records.length;
    reportSource=totalSource=null;
    reportKey=null;
  }

  function setTelemetry(records) {
    telemetry=indexLiveStreams(records);
    streamSource=null;
    streamKey=null;
  }

  function read(filters, now=Date.now()) {
    const nextReportKey=[filters.start,filters.end,filters.mode,filters.version,filters.status];
    if (reportSource!==history || !sameKey(nextReportKey,reportKey)) {
      reports=filterReports(history.reports,filters);
      reportSource=history;
      reportKey=nextReportKey;
    }
    if (totalSource!==reports || totalBots!==filters.bots) {
      totals=aggregate(reports,filters.bots);
      totalSource=reports;
      totalBots=filters.bots;
    }
    const {start,end}=filters.range;
    const nextStreamKey=[start,end,filters.mode];
    const inRange=record=>record.day>=start&&record.day<=end&&(!filters.mode||record.mode===filters.mode);
    if (streamSource!==telemetry || !sameKey(nextStreamKey,streamKey)) {
      streams=telemetry.streams.filter(inRange);
      streamSource=telemetry;
      streamKey=nextStreamKey;
    }
    const sessions=projectLiveRuns(telemetry,now).runs.filter(inRange);
    return {...history,reports,totals,sessions,streams,liveInvalid:telemetry.invalid,documents};
  }

  function reset() { setHistory([]); setTelemetry([]); }

  return {setHistory,setTelemetry,read,reset};
}
