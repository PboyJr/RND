using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Godot;

namespace RND.Players;

/// <summary>
/// Autoload that loads, holds and saves the profile: user://profile.json, or the path given with
/// --profile=&lt;path&gt; on the command line (two copies of the game on one PC, and the tests, use
/// their own). Loaded the first time something asks for it. Saves are written to a temporary file
/// and then moved into place, so a crash mid-save can't leave a half-written profile; a file that
/// won't parse is kept (as .bad) and a fresh profile started, rather than the game refusing to run.
/// </summary>
public partial class ProfileStore : Node
{
	public const string DefaultPath = "user://profile.json";
	private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

	public static ProfileStore Instance { get; private set; }

	/// <summary>Where the profile lives. Set it before anything reads the profile (tests do).</summary>
	public string Path { get; set; } = DefaultPath;

	public Profile Data => _data ??= Load(Path);

	// For the HUD, menus and tests (GDScript can't see the plain Profile class).
	public int Money => Data.Money;
	public int RunCount => Data.RunCount;
	public int Level => Data.ActiveCharacter.Level;
	public int Xp => Data.ActiveCharacter.Xp;
	public int XpToNextLevel => Character.XpToNext(Level);
	public int LastRun => Data.ActiveCharacter.LastRun;

	private Profile _data;

	public override void _EnterTree()
	{
		Instance = this;
		string arg = OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--profile="));
		if (arg != null)
			Path = arg["--profile=".Length..];
	}

	/// <summary>Forget what's loaded; the next read loads from Path again.</summary>
	public void Reload() => _data = null;

	public void StartRun()
	{
		Data.StartRun();
		Save();
	}

	/// <summary>Adds a run's pay and saves. Returns how many levels the active character went up.</summary>
	public int Grant(int money, int xp)
	{
		int levels = Data.Grant(money, xp);
		Save();
		return levels;
	}

	public void Save()
	{
		string path = ProjectSettings.GlobalizePath(Path);
		Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
		File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(Data, Json));
		File.Move(path + ".tmp", path, overwrite: true);
	}

	private static Profile Load(string godotPath)
	{
		string path = ProjectSettings.GlobalizePath(godotPath);
		Profile profile = null;
		if (File.Exists(path))
		{
			try
			{
				profile = JsonSerializer.Deserialize<Profile>(File.ReadAllText(path));
			}
			catch (Exception e) when (e is JsonException or NotSupportedException)
			{
				GD.PushWarning($"Profile at {path} is unreadable ({e.Message}); kept it as .bad and started a fresh one.");
				File.Move(path, path + ".bad", overwrite: true);
			}
		}
		profile ??= new Profile();
		profile.Sanitise();
		return profile;
	}
}
