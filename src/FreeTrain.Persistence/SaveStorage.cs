namespace FreeTrain.Persistence;

// One writer per save directory. IDs are mapped here, never accepted as paths.
public sealed class SaveStorage : IDisposable
{
    private readonly string directory;
    private readonly FileStream lease;
    private readonly SemaphoreSlim writes = new(1);
    public static readonly string[] Slots = ["checkpoint", "slot1", "slot2", "slot3"];
    public SaveStorage(string directory)
    {
        this.directory = Path.GetFullPath(directory);
        Directory.CreateDirectory(this.directory);
        lease = new FileStream(Path.Combine(this.directory, ".writer.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }
    private string PathFor(string id) => Slots.Contains(id, StringComparer.Ordinal)
        ? Path.Combine(directory, id + ".json") : throw new InvalidDataException("invalidSlot");
    public bool Exists(string id) => File.Exists(PathFor(id));
    public SaveDocument Read(string id)
    {
        var path = PathFor(id);
        using var file = File.OpenRead(path);
        if (file.Length > SaveCodec.MaximumBytes) throw new InvalidDataException("saveTooLarge");
        var bytes = new byte[(int)file.Length]; file.ReadExactly(bytes);
        return SaveCodec.Decode(bytes);
    }
    public async Task Write(string id, SaveDocument document)
    {
        var path = PathFor(id); var bytes = SaveCodec.Encode(document);
        if (bytes.Length > SaveCodec.MaximumBytes) throw new InvalidDataException("saveTooLarge");
        await writes.WaitAsync();
        var temporary = Path.Combine(directory, ".save-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            await using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                4096, FileOptions.WriteThrough)) { await file.WriteAsync(bytes); file.Flush(flushToDisk: true); }
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); writes.Release(); }
    }
    public void Dispose() { lease.Dispose(); writes.Dispose(); }
}
