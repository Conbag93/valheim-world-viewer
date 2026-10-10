using System.Text.Json;
using ValheimDiagrams.Core;

// Reads a world save folder and writes the site's demo: summary.json (for the landing page) and
// world.json (what the viewer draws). These hold only what an import keeps in the browser, so none
// of the save's player names, sign text or portal tags are published.
// Usage: dotnet run --project tools/MakeDemo -- <world folder> <output folder> (or scripts/make-demo.sh)
if (args.Length != 2)
{
    Console.Error.WriteLine("Usage: dotnet run --project tools/MakeDemo -- <world folder> <output folder>");
    return 1;
}
var (folder, output) = (args[0], args[1]);

var save = SaveFolder.FromDirectory(folder);
var world = await WorldLoader.LoadAsync(save);

// As for an import: the metadata file's modified time is when the game last saved the world.
var saved = File.GetLastWriteTimeUtc(Path.Combine(folder, save.Latest("fwl2")!));
var summary = StoredSummary.Of(world, saved, DateTime.UtcNow);

Directory.CreateDirectory(output);
await File.WriteAllTextAsync(Path.Combine(output, "summary.json"), JsonSerializer.Serialize(summary, SnapshotJson.Default.StoredSummary));
await File.WriteAllTextAsync(Path.Combine(output, "world.json"), JsonSerializer.Serialize(StoredWorld.From(world), SnapshotJson.Default.StoredWorld));
Console.WriteLine($"{world.Meta.WorldName}: {world.Pieces.Count:N0} pieces, {world.Explored.Count:N0} explored zones, saved {saved:u} → {Path.GetFullPath(output)}");
return 0;
