import test from 'node:test';
import assert from 'node:assert/strict';
import {selectionUsage} from '../web/model.mjs';
import {hasSelectionData,selectionDetails,playerSelection,selectionCsv} from '../web/selection-view.mjs';

test('oculta métricas antiguas o sin modo identificado sin fabricar ceros',()=>{
  for (const record of [{},{elementSelection:{manualSeconds:0,automaticSeconds:0,unreportedSeconds:20}}]) {
    const usage=selectionUsage(record);
    assert.equal(hasSelectionData(usage),false);
    assert.equal(selectionDetails(usage),'');
    assert.equal(playerSelection(record),'');
    assert.deepEqual(selectionCsv(record),['','','','']);
  }
  assert.equal(playerSelection({bot:true}),'');
});
test('muestra tiempos y porcentajes válidos, incluido cero, y omite las métricas desconocidas',()=>{
  const record={elementSelection:{manualSeconds:0,automaticSeconds:60,unreportedSeconds:30}};
  const html=selectionDetails(selectionUsage(record));
  assert.match(html,/Manual/);assert.match(html,/0 s/);assert.match(html,/100 %/);
  assert.doesNotMatch(html,/identificar|Sin datos|30 s/);
  assert.doesNotMatch(playerSelection(record),/identificar|Sin datos|30 s/);
  assert.deepEqual(selectionCsv(record),[0,60,0,100]);
});
