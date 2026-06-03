namespace SnapStudio.Core.Primitives;

public readonly record struct CaptureId(Guid Value)
{
    public static CaptureId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString("D");
}

public readonly record struct DocumentId(Guid Value)
{
    public static DocumentId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString("D");
}
