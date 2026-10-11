using Sandbox;
using Sandbox.Rendering;

/// <summary>
/// Permanent, non-combat PDA carried through the native held-item pipeline.
/// It deliberately derives from <see cref="BaseCombatWeapon"/> only to reuse
/// deployment, viewmodel, world-model and holder animation support.
/// </summary>
[Title( "Void Breach PDA" )]
[Category( "Void Breach/PDA" )]
[Icon( "tablet_mac" )]
public sealed class VBPdaItem : BaseCombatWeapon, IVBFirstPersonHeldItem, IVBInventorySlotPolicy
{
	public int RequiredInventorySlot => VBWeaponSelectionController.PdaSlot;
	public bool LocksInventorySlot => true;
	public bool IsInventoryBound => true;
	public GameObject FirstPersonViewModel => ViewModel;
	public float FirstPersonMotionAimAmount => 0f;

	public override bool CanPrimaryAttack() => false;
	public override bool CanSecondaryAttack() => false;

	public override void PrimaryAttack()
	{
	}

	public override void SecondaryAttack()
	{
	}

	public override void DrawHud( HudPainter hud, Vector2 center )
	{
	}

	protected override void OnUpdate()
	{
		if ( Application.IsDedicatedServer )
			return;

		if ( !IsActive )
		{
			DestroyWorldModel();
			return;
		}

		base.OnUpdate();

		if ( IsHeld && !WorldModel.IsValid() )
			CreateWorldModel();
	}

	protected override bool OnDrop() => false;
}
