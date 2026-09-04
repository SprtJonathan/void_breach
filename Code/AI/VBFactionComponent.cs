using System;
using Sandbox;

/// <summary>
/// Lightweight faction identity shared by players and NPCs. Relations are
/// declared by the observer so asymmetrical hostility remains possible.
/// </summary>
[Title( "Faction Member" )]
[Category( "Void Breach/AI" )]
[Icon( "groups" )]
public sealed class VBFactionComponent : Component
{
	public const string NeutralFaction = "neutral";
	public const string PlayerFaction = "player";
	public const string CartelFaction = "cartel";
	public const string XenFaction = "xen";
	public const string BossFaction = "boss";
	[Property] public string FactionId { get; set; } = "neutral";
	[Property, Sync( SyncFlags.FromHost ), Description( "Identifiant d'équipe dynamique. Vide tant que le service d'équipes n'est pas branché." )]
	public string TeamId { get; set; } = string.Empty;
	[Property] public List<string> HostileFactions { get; set; } = new();
	[Property] public List<string> IgnoredFactions { get; set; } = new();
	[Property, Description( "Deux acteurs de la même faction deviennent hostiles si leurs TeamId non vides diffèrent." )]
	public bool HostileToOtherTeams { get; set; }
	[Property] public bool IsTargetable { get; set; } = true;

	public bool IsConfigured => !string.IsNullOrWhiteSpace( FactionId )
		&& !string.Equals( FactionId, NeutralFaction, StringComparison.OrdinalIgnoreCase );

	public void ConfigureDefaults( string factionId, params string[] hostileFactions )
	{
		if ( !IsConfigured )
			FactionId = factionId;

		if ( HostileFactions.Count == 0 )
			HostileFactions.AddRange( hostileFactions );

	}

	public bool IsHostileTo( VBFactionComponent other )
	{
		if ( !other.IsValid() || !other.IsTargetable )
			return false;

		if ( ContainsFaction( IgnoredFactions, other.FactionId ) )
			return false;

		if ( string.Equals( FactionId, other.FactionId, StringComparison.OrdinalIgnoreCase ) )
		{
			return HostileToOtherTeams
				&& !string.IsNullOrWhiteSpace( TeamId )
				&& !string.IsNullOrWhiteSpace( other.TeamId )
				&& !string.Equals( TeamId, other.TeamId, StringComparison.OrdinalIgnoreCase );
		}

		return ContainsFaction( HostileFactions, other.FactionId );
	}

	private static bool ContainsFaction( IEnumerable<string> factions, string factionId )
	{
		return factions.Any( faction =>
			string.Equals( faction, factionId, StringComparison.OrdinalIgnoreCase ) );
	}
}
