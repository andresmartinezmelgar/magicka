namespace Arcana.Core;

/// <summary>Reaplica solo las pulsaciones que aún no ha procesado la simulación.</summary>
public sealed class PredictedElementSelection
{
    private readonly ElementBuffer _selection = new();
    private readonly Queue<(long Sequence, string Type, Element Element)> _pending = new();
    public IReadOnlyList<Element> Elements => _selection.Elements;
    public bool Pending => _pending.Count > 0;

    public void Reset()
    {
        _pending.Clear();
        _selection.Clear();
    }

    public void Predict(ClientMessage message)
    {
        Apply(message.Type, message.Element);
    }

    public void Track(ClientMessage message)
    {
        if (message.Sequence > 0) _pending.Enqueue((message.Sequence, message.Type, message.Element));
    }

    public void Confirm(IReadOnlyList<Element> authoritative, long sequence)
    {
        while (_pending.TryPeek(out var action) && action.Sequence <= sequence) _pending.Dequeue();
        _selection.Clear();
        foreach (var element in authoritative) _selection.Add(element);
        foreach (var action in _pending) Apply(action.Type, action.Element);
    }

    private void Apply(string type, Element element)
    {
        if (type == "element") _selection.Add(element);
        else if (type is "clear" or "cast" or "self" or "nova") _selection.Clear();
    }
}
