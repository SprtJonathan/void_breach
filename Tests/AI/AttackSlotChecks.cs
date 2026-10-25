using System;
using System.Collections.Generic;

public static class AttackSlotChecks
{
	private static int _checks;
	private static void Check( bool pass, string label )
	{
		if ( !pass ) throw new Exception( label );
		_checks++;
		Console.WriteLine( $"PASS: {label}" );
	}
	public static void Main()
	{
		var pool = new VBAttackSlotPool<string, int>();
		bool Ask( string player, int soldier, float now, int limit = 2 ) => pool.RequestTurn( player, soldier, now, limit, 1f, 0.35f );
		Check( Ask( "A", 1, 0f ) && Ask( "A", 2, 0f ) && !Ask( "A", 3, 0f ), "two shooters share a player budget; third waits" );
		Check( Ask( "B", 4, 0f ) && Ask( "B", 5, 0f ) && !Ask( "B", 6, 0f ), "another player has an independent budget" );
		pool.Release( 1 );
		Check( Ask( "A", 3, 0.1f ), "releasing a shooter immediately admits the waiting soldier" );
		Check( Ask( "B", 2, 0.1f ) == false && Ask( "A", 7, 0.1f ), "switching target releases the previous player's slot" );
		Check( Ask( "B", 8, 0.6f ), "expired requests cannot strand capacity" );
		pool.RemoveInvalid( ( target, actor ) => actor != 8 );
		Check( Ask( "B", 9, 0.6f ), "destroyed or disabled holders can be purged" );
		Check( !Ask( "A", 3, 0.6f, 0 ), "zero budget disables ranged attacks against that player" );

		pool = new();
		var participants = new HashSet<int>();
		bool capHeld = true, targetsIndependent = false;
		for ( int frame = 0; frame < 500; frame++ )
		{
			float now = frame * 0.02f;
			int first = 0, second = 0;
			// Deliberately reverse actor order on alternate frames.
			for ( int i = 0; i < 12; i++ )
			{
				int actor = frame % 2 == 0 ? i : 11 - i;
				bool granted = Ask( actor < 6 ? "A" : "B", actor, now );
				if ( !granted ) continue;
				participants.Add( actor );
				if ( actor < 6 ) first++; else second++;
			}
			capHeld &= first <= 2 && second <= 2;
			targetsIndependent |= first == 2 && second == 2;
		}
		Check( capHeld, "both player caps hold through 500 frames and reordered updates" );
		Check( targetsIndependent, "four soldiers may engage two different players together" );
		Check( participants.Count == 12, "all waiting soldiers receive a turn without starvation" );
		pool = new();
		Check( Ask( "A", 1, 0f ) && Ask( "A", 2, 0f ) && Ask( "A", 1, 0.1f, 1 ) && !Ask( "A", 2, 0.1f, 1 ),
			"lowering the budget revokes excess turns immediately" );
		Console.WriteLine( $"Attack slots FINISHED: {_checks} checks, 0 failures." );
	}
}
