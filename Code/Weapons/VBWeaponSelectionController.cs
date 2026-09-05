using System;
using System.Linq;
using Sandbox;

/// <summary>
/// Maps the project's complete six-slot loadout to the native s&amp;box inventory
/// switch API. Slot ownership, deployment and network authority remain handled by
/// <see cref="BaseInventoryComponent"/>.
/// </summary>
[Title( "Void Breach Weapon Selection" )]
[Category( "Void Breach/Weapons" )]
[Icon( "swap_horiz" )]
public sealed class VBWeaponSelectionController : Component
{
	public const int FirstWeaponSlot = 0;
	public const int WeaponSlotCount = 2;
	public const int ThrowableSlot = 2;
	public const int MedicalSlot = 3;
	public const int UtilitySlot = 4;
	public const int PdaSlot = 5;
	public const int InventorySlotCount = 6;

	[Property, Group( "HUD" ), Range( 0.25f, 5f ), Step( 0.05f )]
	public float SelectionHudDuration { get; set; } = 1.5f;

	public bool IsSelectionHudVisible => _hasSelectionInput
		&& _timeSinceSelectionInput < SelectionHudDuration;

	/// <summary>
	/// Logical inventory slot selected by the player. Empty weapon slots resolve
	/// to the shared fists placeholder.
	/// </summary>
	public int SelectedSlot { get; private set; } = FirstWeaponSlot;

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

		if ( Input.Pressed( "Slot3" ) )
		{
			ShowSelectionHud();
			SelectSlot( ThrowableSlot );
			return;
		}

		if ( Input.Pressed( "Slot4" ) )
		{
			ShowSelectionHud();
			SelectSlot( MedicalSlot );
			return;
		}

		if ( Input.Pressed( "Slot5" ) )
		{
			ShowSelectionHud();
			SelectSlot( UtilitySlot );
			return;
		}

		if ( Input.Pressed( "Slot6" ) )
		{
			ShowSelectionHud();
			SelectSlot( PdaSlot );
			return;
		}

		if ( Input.Pressed( "SlotNext" ) )
		{
			ShowSelectionHud();
			CycleItem( 1 );
			return;
		}

		if ( Input.Pressed( "SlotPrev" ) )
		{
			ShowSelectionHud();
			CycleItem( -1 );
		}
	}

	private void ShowSelectionHud()
	{
		_hasSelectionInput = true;
		_timeSinceSelectionInput = 0f;
	}

	private void SelectSlot( int slot )
	{
		SelectedSlot = Math.Clamp(
			slot,
			FirstWeaponSlot,
			InventorySlotCount - 1
		);
		SwitchTo( ItemInSlot( SelectedSlot ) );
	}

	private void CycleItem( int direction )
	{
		for ( var step = 1; step <= InventorySlotCount; step++ )
		{
			var nextSlot = (SelectedSlot + direction * step + InventorySlotCount)
				% InventorySlotCount;
			var item = ItemInSlot( nextSlot );

			if ( !item.IsValid() || !item.CanSwitchTo() )
				continue;

			SelectSlot( nextSlot );
			return;
		}
	}

	/// <summary>
	/// Resolves every slot exposed by the Void Breach selector. Weapon slots
	/// use fists as their empty state; equipment slots remain empty until an
	/// item of the corresponding category is present.
	/// </summary>
	public BaseInventoryItem ItemInSlot( int slot )
	{
		if ( slot >= FirstWeaponSlot && slot < FirstWeaponSlot + WeaponSlotCount )
			return WeaponInSlot( slot );

		return slot >= ThrowableSlot && slot < InventorySlotCount
			? Inventory.GetSlot( slot )
			: null;
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
			if ( !VBWeaponInventoryComponent.IsCarriedWeapon( weapon ) )
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
		if ( Inventory.ActiveItem is not BaseInventoryItem active )
			return;

		if ( active is BaseCombatWeapon activeWeapon )
		{
			if ( VBWeaponInventoryComponent.IsPlaceholder( activeWeapon ) )
				return;

			for ( var slot = FirstWeaponSlot; slot < FirstWeaponSlot + WeaponSlotCount; slot++ )
			{
				if ( RealWeaponInSlot( slot ) != activeWeapon )
					continue;

				SelectedSlot = slot;
				return;
			}

			return;
		}

		if ( active.Slot >= ThrowableSlot && active.Slot < InventorySlotCount )
			SelectedSlot = active.Slot;
	}

	private void SwitchTo( BaseInventoryItem item )
	{
		if ( !item.IsValid() || !item.CanSwitchTo() )
			return;

		if ( item == Inventory.ActiveItem )
			return;

		Inventory.Switch( item, allowHolster: false );
	}
}
