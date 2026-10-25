using System;
using Sandbox;

public sealed partial class VBSoldierController
{
	[ConCmd( "vb_soldier_debug" )]
	public static void DumpSoldiers()
	{
		var scene = Game.ActiveScene;
		if ( scene is null ) return;
		foreach ( var soldier in scene.GetAllComponents<VBSoldierController>() )
		{
			var inventory = soldier.Inventory;
			var target = soldier.Perception.IsValid() ? soldier.Perception.CurrentTarget : null;
			Log.Info( $"[Cartel Soldier] {soldier.GameObject.Name} host={Networking.IsHost} active={soldier.Active} canFight={soldier.CanFight} state={soldier.State} ragdoll={soldier.Ragdoll.State} target={target?.Name ?? "none"} visible={soldier.Perception.TargetVisible} agentEnabled={soldier.Agent.Enabled} navigating={soldier.Agent.IsNavigating} link={soldier.Agent.IsTraversingLink} path={soldier._lastPathStatus} inventories={soldier.GetComponents<BaseInventoryComponent>( includeDisabled: true ).Count()} activeItem={inventory?.ActiveItem?.GameObject.Name ?? "none"}" );
			if ( !inventory.IsValid() ) continue;
			var traversal = soldier.GetComponent<VBNavLinkTraversal>();
			Log.Info( $"  coordination: fireSlot={soldier.HasFireSlot} playerLimit={soldier.Coordination.MaxShootersPerPlayer} turn={soldier.Coordination.FireTurnDuration:F1}s aimYaw={soldier.WorldRotation.Yaw():F1} velocity={soldier.Locomotion.Velocity}" );
			Log.Info( $"  awareness: suspicion={soldier.Perception.HasVisualSuspicion} report={soldier.Perception.ReportedTarget?.Name ?? "none"} reportAge={Time.Now - soldier.Perception.ReportedAt:F1}s alertWait={MathF.Max( 0f, soldier._alertObserveUntil - Time.Now ):F1}s watching={soldier._watchingSector}" );
			foreach ( var candidate in scene.GetAllComponents<VBHealthComponent>() )
			{
				if ( !VBSoldierCoordination.IsPlayerTarget( candidate.GameObject ) ) continue;
				var delta = candidate.WorldPosition - soldier.WorldPosition;
				Log.Info( $"  player sight: {candidate.GameObject.Name} viable={soldier.Perception.IsViableTarget( candidate.GameObject )} canSee={soldier.Perception.CanSee( candidate.GameObject )} distance={delta.Length:F0} forwardDot={Vector3.Dot( soldier.WorldRotation.Forward, delta.WithZ( 0f ).Normal ):F2} fov={soldier.Perception.FieldOfView:F0} closeFov={soldier.CloseAwarenessFieldOfView:F0}" );
			}
			Log.Info( $"  shelter: cover={soldier._hasCover} crouch={soldier.IsCrouching} peek={soldier._peeking} peekAvailable={soldier._coverHasPeek} supportRoute={soldier._supportRoute} destination={soldier._destination}" );
			Log.Info( $"  engagement: reason={soldier._combatReason} evidenceAge={soldier.ContactEvidenceAge:F1}s reconnect={soldier._recoveringContact} goal={soldier._recoveryPosition} deadline={MathF.Max( 0f, soldier._recoveryDeadline - Time.Now ):F1}s pressure={MathF.Max( 0f, Time.Now - soldier._pressureSince ):F1}s" );
			Log.Info( $"  firing stance: stabilized={soldier.IsInFiringStance} spreadMultiplier={soldier.FiringStanceAccuracyMultiplier:F2} settleWait={MathF.Max( 0f, soldier.FiringStanceSettled ):F2}" );
			Log.Info( $"  tempo: attention={soldier.RemembersTarget( target )} contactAge={Time.Now - soldier.Perception.LastSeenAt:F1} hold={MathF.Max( 0f, soldier._holdPositionUntil - Time.Now ):F1} maneuverLimit={soldier.Coordination.MaxManeuversPerPlayer} acceleration={soldier.MovementAcceleration:F0}" );
			Log.Info( $"  pacing: burstWait={MathF.Max( 0f, soldier._nextBurst - Time.Now ):F2} aimWait={(float)soldier.FireReady:F2} speed={soldier.Agent.MaxSpeed:F0} tactical={soldier._tacticalMove} flank={soldier._flanking} climb={traversal?.IsClimbing} linkFailure={traversal?.LastFailure ?? "none"}" );
			foreach ( var item in inventory.Items.OfType<BaseCombatWeapon>() )
				Log.Info( $"  {item.GameObject.Name} slot={item.Slot} active={item.IsActive} clip={item.Clip1} reserve={item.Ammo1} reload={item.IsReloading} canFire={item.CanPrimaryAttack()}" );
		}
	}
}
