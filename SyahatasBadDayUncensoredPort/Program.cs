// See https://aka.ms/new-console-template for more information

using System.Diagnostics.CodeAnalysis;

using AssetStudio;

string? oldGamePath = null;
string? newGamePath = null;

Console.Write("Please enter the data path of the old version of the game (1.0.1 or 1.0.2): ");
oldGamePath = Console.ReadLine();
EnsurePath(oldGamePath);
Console.Write("Please enter the data path of the new version of the game: ");
newGamePath = Console.ReadLine();
EnsurePath(newGamePath);

Logger.Default = new AssetStudioLogger();

var oldGameAssets = new AssetsManager
{
    Game = new Game(GameType.Normal)
};
var newGameAssets = new AssetsManager
{
    Game = new Game(GameType.Normal)
};

{
    Info("Parsing game assets, it may take a while...");
    var oldGameLoadTask = Task.Run(() => oldGameAssets.LoadFolder(oldGamePath));
    var newGameLoadTask = Task.Run(() => newGameAssets.LoadFolder(newGamePath));
    await oldGameLoadTask;
    await newGameLoadTask;
}

var oldGameVideoClips = oldGameAssets.assetsFileList
    .SelectMany(assetsFile => assetsFile.Objects
        .OfType<VideoClip>())
    .Where(clip => clip.m_OriginalPath.Contains("/uncensored/"))
    .ToList();
var newGameVideoClips = newGameAssets.assetsFileList
    .SelectMany(assetsFile => assetsFile.Objects
        .OfType<VideoClip>())
    .Where(clip => clip.m_OriginalPath.Contains("/censored/"))
    .ToList();

var oldClipsDic = oldGameVideoClips.ToDictionary(clip => clip.m_Name);

var newGroups = newGameVideoClips.GroupBy(clip => clip.m_ExternalResources.m_Source).ToArray();

Info($"Find {newGameVideoClips.Count} censored clips in {newGroups.Length} files, and {oldGameVideoClips.Count} uncensored clips.");
CloseAllFiles(newGameAssets);
foreach (var group in newGroups)
{
    var clips = group.ToArray();
    // Sort by offset
    Array.Sort(clips, (a, b) => a.m_ExternalResources.m_Offset.CompareTo(b.m_ExternalResources.m_Offset));

    using var newResourceFile = File.OpenWrite(Path.Combine(newGamePath, group.Key));
    foreach (var clip in clips)
    {
        if (!oldClipsDic.TryGetValue(clip.m_Name, out var oldClip))
        {
            Warn($"Could not find the corresponding uncensored clip for the censored clip: {clip.m_Name}");
            continue;
        }

        Info($"Replacing {clip.m_Name} in {group.Key}...");
        long offset = newResourceFile.Length;
        long length = oldClip.m_ExternalResources.m_Size;

        using (var assetFile = File.OpenWrite(clip.assetsFile.fullName))
        using (var assetFileWriter = new BinaryWriter(assetFile))
        {
            assetFileWriter.Seek((int)(clip.reader.byteStart + clip.reader.byteSize - 18), SeekOrigin.Begin);
            assetFileWriter.Write(offset);
            assetFileWriter.Write(length);
        }

        newResourceFile.Seek(offset, SeekOrigin.Begin);
        newResourceFile.Write(oldClip.m_VideoData.GetData());
    }
}

Console.WriteLine("All done! Press any key to exit.");
Console.ReadKey();
return;

void Info(string message)
{
    Console.WriteLine($"info: {message}");
}

void Warn(string message)
{
    var oldColor = Console.ForegroundColor;
    Console.ForegroundColor = ConsoleColor.Yellow;
    Console.WriteLine($"warn: {message}");
    Console.ForegroundColor = oldColor;
}

void Fatal(string message)
{
    var oldColor = Console.ForegroundColor;
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine($"error: {message}");
    Console.ForegroundColor = oldColor;
    Console.WriteLine("The program will now exit. (Press any key to continue)");
    Console.ReadKey();
    Environment.Exit(0);
}

void EnsurePath([NotNull] string? path)
{
    if (!Path.Exists(path))
    {
        Fatal($"The path '{path}' does not exist.");
        throw new Exception("Unreachable code");
    }
}

void CloseAllFiles(AssetsManager manager)
{
    foreach (var assetsFile in manager.assetsFileList)
    {
        assetsFile.reader.BaseStream.Dispose();
        assetsFile.reader.Dispose();
    }
}

class AssetStudioLogger : ILogger
{
    public void Log(LoggerEvent loggerEvent, string message)
    {
        if (loggerEvent < LoggerEvent.Warning)
            return;

        Console.WriteLine($"[AssetStudio][{loggerEvent}] {message}");
    }
}