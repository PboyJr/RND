using System;
using System.Collections.Generic;

namespace RND.Players;

/// <summary>
/// A player's saved progress (see DESIGN → How player data is stored). Only what's "in the
/// scientist's head" is saved: money (per profile) and a roster of characters with their level and
/// XP. What the rat carried is never saved. Plain data, serialised as JSON by ProfileStore.
/// </summary>
public sealed class Profile
{
	public const int CurrentVersion = 1;
	public const int RetrainRuns = 3;  // a run on a character moves its LastRun forward this far (K in DESIGN)
	public const int MaxLevel = 50;

	public int Version { get; set; } = CurrentVersion;
	/// <summary>Runs started, on any character. Goes up (and is saved) when a run starts.</summary>
	public int RunCount { get; set; }
	public int Money { get; set; }
	public int Active { get; set; }
	public List<Character> Characters { get; set; } = [new Character()];

	public Character ActiveCharacter => Characters[Math.Clamp(Active, 0, Characters.Count - 1)];

	/// <summary>A run is starting on the active character: count it, and let that character shed some rust.</summary>
	public void StartRun()
	{
		RunCount++;
		ActiveCharacter.LastRun = Math.Min(ActiveCharacter.LastRun + RetrainRuns, RunCount);
	}

	/// <summary>Adds a run's pay. Returns how many levels the active character went up.</summary>
	public int Grant(int money, int xp)
	{
		Money += Math.Max(money, 0);
		Character c = ActiveCharacter;
		c.Xp += Math.Max(xp, 0);
		int levelsGained = 0;
		while (c.Level < MaxLevel && c.Xp >= Character.XpToNext(c.Level))
		{
			c.Xp -= Character.XpToNext(c.Level);
			c.Level++;
			levelsGained++;
		}
		return levelsGained;
	}

	/// <summary>Fixes up anything a hand-edited or older file got wrong, so the game can trust it.</summary>
	public void Sanitise()
	{
		if (Characters == null || Characters.Count == 0)
			Characters = [new Character()];
		Active = Math.Clamp(Active, 0, Characters.Count - 1);
		RunCount = Math.Max(RunCount, 0);
		Money = Math.Max(Money, 0);
		foreach (Character c in Characters)
		{
			c.Level = Math.Clamp(c.Level, 1, MaxLevel);
			c.Xp = Math.Max(c.Xp, 0);
			c.LastRun = Math.Clamp(c.LastRun, 0, RunCount);
			c.Upgrades ??= [];
			c.Abilities ??= [];
		}
		Version = CurrentVersion;
	}
}

public sealed class Character
{
	public string Id { get; set; } = Guid.NewGuid().ToString("N");
	public string Name { get; set; } = "Researcher";
	public string Archetype { get; set; } = "Scientist";
	public int Level { get; set; } = 1;
	public int Xp { get; set; }
	public int Rebirths { get; set; }
	public List<string> Upgrades { get; set; } = [];
	public Dictionary<string, int> Abilities { get; set; } = [];
	/// <summary>Profile.RunCount as of this character's last run (roughly). Rust = RunCount − LastRun.</summary>
	public int LastRun { get; set; }

	public static int XpToNext(int level) => 100 * level;
}

