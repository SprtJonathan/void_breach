using System;
using System.Linq;
using Sandbox;

/// <summary>
/// Maps the project's two weapon slots to the native s&amp;box inventory switch API.
/// Slot ownership, deployment and network authority remain handled by
/// <see cref="BaseInventoryComponent"/>.
/// </summary>
[Title( "Void Breach Weapon Selection" )]
[Category( "Void Breach/Weapons" )]
[Icon( "swap_horiz" )]
public sealed class VBWeaponSelectionController : Component
{
	public const int FirstWeaponSlot = 0;
	public const int WeaponSlotCount = 2;

	[Property, Group( "HUD" ), Range( 0.25f, 5f ), Step( 0.05f )]
	public float SelectionHudDuration { get; set; } = 1.5f;

	public bool IsSelectionHudVisible => _hasSelectionInput
		&& _timeSinceSelectionInput < SelectionHudDuration;

	/// <summary>
	/// Logical weapon slot selected by the player. Empty slots resolve to the
	/// shared fists placeholder.
	/// </summary>
	public int SelectedWeaponSlot { get; private set; } = FirstWeaponSlot;

	[RequireComponent]
	private BaseInventoryComponent Inventory { get; set; }

	private TimeSince _timeSinceSelectionInput;
	private bool _hasSelectionInput;

	protected override void OnUpdate()
	{
		if ( IsProxy || !Inventory.IsValid() || !Inventory.Enabled )
			return;

		SynchronizeSelectedSlot();

		if ( Input.Pressed( "Slot1" ) )
		{
			ShowSelectionHud();
			SelectSlot( FirstWeaponSlot );
			return;
		}

		if ( Input.Pressed( "Slot2" ) )
		{
			ShowSelectionHud();
			SelectSlot( FirstWeaponSlot + 1 );
			return;
		}

		if ( Input.Pressed( "SlotNext" ) )
		{
			ShowSelectionHud();
			CycleWeapon( 1 );
			return;
		}

		if ( Input.Pressed( "SlotPrev" ) )
		{
			ShowSelectionHud();
			CycleWeapon( -1 );
		}
	}

	private void ShowSelectionHud()
	{
		_hasSelectionInput = true;
		_timeSinceSelectionInput = 0f;
	}

	private void SelectSlot( int slot )
	{
		SelectedWeaponSlot = Math.Clamp(
			slot,
			FirstWeaponSlot,
			FirstWeaponSlot + WeaponSlotCount - 1
		);
		SwitchTo( WeaponInSlot( SelectedWeaponSlot ) );
	}

	private void CycleWeapon( int direction )
	{
		var relativeSlot = SelectedWeaponSlot - FirstWeaponSlot;
		var nextSlot = (relativeSlot + direction + WeaponSlotCount) % WeaponSlotCount;
		SelectSlot( FirstWeaponSlot + nextSlot );
	}

	/// <summary>
	/// Returns the real weapon assigned to a logical slot, or the permanent
	/// fists item when that slot is empty.
	/// </summary>
	public BaseCombatWeapon WeaponInSlot( int slot )
	{
		if ( !Inventory.IsValid() )
			return null;

		return RealWeaponInSlot( slot ) ?? Inventory.Items
			.OfType<BaseCombatWeapon>()
			.FirstOrDefault( VBWeaponInventoryComponent.IsPlaceholder );
	}

	private BaseCombatWeapon RealWeaponInSlot( int requestedSlot )
	{
		ResolveRealWeaponSlots( out var slotA, out var slotB );

		return requestedSlot switch
		{
			FirstWeaponSlot => slotA,
			FirstWeaponSlot + 1 => slotB,
			_ => null
		};
	}

	private void ResolveRealWeaponSlots(
		out BaseCombatWeapon slotA,
		out BaseCombatWeapon slotB
	)
	{
		slotA = null;
		slotB = null;
		BaseCombatWeapon overflow = null;

		foreach ( var weapon in Inventory.Items.OfType<BaseCombatWeapon>() )
		{
			if ( VBWeaponInventoryComponent.IsPlaceholder( weapon ) )
				continue;

			if ( weapon.Slot == FirstWeaponSlot )
			{
				AssignPreferred( ref slotA, ref overflow, weapon );
			}
			else if ( weapon.Slot == FirstWeaponSlot + 1 )
			{
				AssignPreferred( ref slotB, ref overflow, weapon );
			}
			else
			{
				overflow = EarlierWeapon( overflow, weapon );
			}
		}

		if ( slotA is null )
		{
			slotA = overflow;
			overflow = null;
		}

		if ( slotB is null )
			slotB = overflow;
	}

	private static void AssignPreferred(
		ref BaseCombatWeapon slot,
		ref BaseCombatWeapon overflow,
		BaseCombatWeapon candidate
	)
	{
		var earlier = EarlierWeapon( slot, candidate );
		var displaced = earlier == candidate ? slot : candidate;
		slot = earlier;

		if ( displaced.IsValid() )
			overflow = EarlierWeapon( overflow, displaced );
	}

	private static BaseCombatWeapon EarlierWeapon(
		BaseCombatWeapon current,
		BaseCombatWeapon candidate
	)
	{
		if ( !current.IsValid() )
			return candidate;

		if ( !candidate.IsValid() )
			return current;

		if ( candidate.SlotOrder != current.SlotOrder )
			return candidate.SlotOrder < current.SlotOrder ? candidate : current;

		return candidate.Id.CompareTo( current.Id ) < 0 ? candidate : current;
	}

	public bool IsSlotPlaceholder( int slot )
	{
		var weapon = WeaponInSlot( slot );
		return VBWeaponInventoryComponent.IsPlaceholder( weapon );
	}

	private void SynchronizeSelectedSlot()
	{
		if ( Inventory.ActiveItem is not BaseCombatWeapon active
			|| VBWeaponInventoryComponent.IsPlaceholder( active ) )
			return;

		for ( var slot = FirstWeaponSlot; slot < FirstWeaponSlot + WeaponSlotCount; slot++ )
		{
			if ( RealWeaponInSlot( slot ) != active )
				continue;

			SelectedWeaponSlot = slot;
			return;
		}
	}

	private void SwitchTo( BaseCombatWeapon weapon )
	{
		if ( !weapon.IsValid() || !weapon.CanSwitchTo() )
			return;

		if ( weapon == Inventory.ActiveItem )
			return;

		Inventory.Switch( weapon, allowHolster: false );
	}
}
