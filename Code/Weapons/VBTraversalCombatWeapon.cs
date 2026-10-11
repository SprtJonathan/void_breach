using Sandbox;

/// <summary>Shared traversal lock for weapon input, direct actions and host shot validation.</summary>
public abstract class VBTraversalCombatWeapon : BaseCombatWeapon
{
	protected bool IsTraversalBlockingActions => Owner.IsValid()
		&& Owner.Components.Get<VBMoveModeTraversal>() is { IsTraversing: true };

	private bool _reloadCancelRequested;

	protected override bool WantsPrimaryAttack() => !IsTraversalBlockingActions && base.WantsPrimaryAttack();
	protected override bool WantsSecondaryAttack() => !IsTraversalBlockingActions && base.WantsSecondaryAttack();
	protected override bool WantsReload() => !IsTraversalBlockingActions && base.WantsReload();
	public override bool CanPrimaryAttack() => !IsTraversalBlockingActions && base.CanPrimaryAttack();
	public override bool CanSecondaryAttack() => !IsTraversalBlockingActions && base.CanSecondaryAttack();
	public override bool CanReload() => !IsTraversalBlockingActions && base.CanReload();

	public override void PrimaryAttack()
	{
		if ( IsTraversalBlockingActions ) return;
		base.PrimaryAttack();
	}

	public override void SecondaryAttack()
	{
		if ( IsTraversalBlockingActions ) return;
		base.SecondaryAttack();
	}

	public override void Reload()
	{
		if ( IsTraversalBlockingActions ) return;
		base.Reload();
	}

	protected override bool OnValidateShotClaim( in ShotClaim claim )
		=> !IsTraversalBlockingActions && base.OnValidateShotClaim( in claim );

	protected override void OnFixedUpdate()
	{
		// Reloads run asynchronously on the host. Stop an existing reload too,
		// including for a remote owner; send at most one owner request while waiting.
		if ( !IsReloading || !IsTraversalBlockingActions )
		{
			_reloadCancelRequested = false;
			return;
		}
		if ( !Networking.IsHost && (IsProxy || _reloadCancelRequested) ) return;
		_reloadCancelRequested = true;
		CancelReload();
	}
}
