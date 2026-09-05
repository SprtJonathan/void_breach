using System;
using System.Linq;
using Sandbox;

/// <summary>
/// Maps the project's complete seven-slot loadout to the native s&amp;box inventory
/// switch API. Slot ownership, deployment and network authority remain handled by
/// <see cref="BaseInventoryComponent"/>.
/// </summary>
[Title( "Void Breach Weapon Selection" )]
[Category( "Void Breach/Weapons" )]
[Icon( "swap_horiz" )]
public sealed class VBWeaponSelectionController : Component
{
	public const int FirstWeaponSlot = 0;
	public const int PrimaryWeaponSlotCount = 2;
	public const int MeleeWeaponSlot = 2;
	public const int WeaponSlotCount = 3;
	public const int ThrowableSlot = 3;
	public const int MedicalSlot = 4;
	public const int UtilitySlot = 5;
	public const int PdaSlot = 6;
	public const int InventorySlotCount = 7;

	[Property, Group( "HUD" ), Range( 0.25f, 5f ), Step( 0.05f )]
	public float SelectionHudDuration { get; set; } = 1.5f;

	[Property, Group( "Drop" ), InputAction]
	public string DropWeaponInputAction { get; set; } = "DropWeapon";

	[Property, Group( "Drop" ), Range( 0.25f, 3f ), Step( 0.05f )]
	public float DropWeaponHoldDuration { get; set; } = 0.85f;

	public bool IsSelectionHudVisible => _hasSelectionInput
		&& _timeSinceSelectionInput < SelectionHudDuration;

	public bool IsDropHoldActive => _isHoldingDrop
		&& Inventory?.ActiveItem is BaseCombatWeapon activeWeapon
		&& VBWeaponInventoryComponent.IsCarriedWeapon( activeWeapon );

	public float DropHoldProgress => IsDropHoldActive
		? Math.Clamp( (float)_timeSinceDropPressed / MathF.Max( DropWeaponHoldDuration, 0.01f ), 0f, 1f )
		: 0f;

	/// <summary>
	/// Logical inventory slot selected by the player. Empty weapon slots resolve
	/// to the shared fists placeholder.
	/// </summary>
	public int SelectedSlot { get; private set; } = FirstWeaponSlot;

	[RequireComponent]
	private BaseInventoryComponent Inventory { get; set; }

	private TimeSince _timeSinceSelectionInput;
	private TimeSince _timeSinceDropPressed;
	private bool _hasSelectionInput;
	private bool _isHoldingDrop;

	protected override void OnUpdate()
	{
		if ( IsProxy || !Inventory.IsValid() || !Inventory.Enabled )
			return;

		SynchronizeSelectedSlot();
		UpdateDropInput();

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
			SelectSlot( MeleeWeaponSlot );
			return;
		}

		if ( Input.Pressed( "Slot4" ) )
		{
			ShowSelectionHud();
			SelectSlot( ThrowableSlot );
			return;
		}

		if ( Input.Pressed( "Slot5" ) )
		{
			ShowSelectionHud();
			SelectSlot( MedicalSlot );
			return;
		}

		if ( Input.Pressed( "Slot6" ) )
		{
			ShowSelectionHud();
			SelectSlot( UtilitySlot );
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

	private void UpdateDropInput()
	{
		if ( string.IsNullOrWhiteSpace( DropWeaponInputAction ) )
		{
			_isHoldingDrop = false;
			return;
		}

		if ( Input.Pressed( DropWeaponInputAction ) )
		{
			_isHoldingDrop = true;
			_timeSinceDropPressed = 0f;
			ShowSelectionHud();
		}

		if ( !_isHoldingDrop )
			return;

		if ( !Input.Down( DropWeaponInputAction ) )
		{
			_isHoldingDrop = false;
			return;
		}

		ShowSelectionHud();

		if ( _timeSinceDropPressed < DropWeaponHoldDuration )
			return;

		_isHoldingDrop = false;

		if ( Inventory.ActiveItem is BaseCombatWeapon weapon
			&& VBWeaponInventoryComponent.IsCarriedWeapon( weapon ) )
		{
			Inventory.Drop( weapon );
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
		var relativeSlot = requestedSlot - FirstWeaponSlot;
		if ( relativeSlot < 0 || relativeSlot >= WeaponSlotCount )
			return null;

		return ResolveRealWeaponSlots()[relativeSlot];
	}

	private BaseCombatWeapon[] ResolveRealWeaponSlots()
	{
		var resolvedSlots = new BaseCombatWeapon[WeaponSlotCount];
		var overflow = new System.Collections.Generic.List<BaseCombatWeapon>();
		var weapons = Inventory.Items
			.OfType<BaseCombatWeapon>()
			.Where( VBWeaponInventoryComponent.IsCarriedWeapon )
			.OrderBy( weapon => weapon.SlotOrder )
			.ThenBy( weapon => weapon.Id );

		foreach ( var weapon in weapons )
		{
			var relativeSlot = weapon.Slot - FirstWeaponSlot;
			if ( relativeSlot >= 0
				&& relativeSlot < WeaponSlotCount
				&& !resolvedSlots[relativeSlot].IsValid() )
				resolvedSlots[relativeSlot] = weapon;
			else
				overflow.Add( weapon );
		}

		var overflowIndex = 0;
		for ( var slot = 0; slot < resolvedSlots.Length && overflowIndex < overflow.Count; slot++ )
		{
			if ( resolvedSlots[slot].IsValid() )
				continue;

			resolvedSlots[slot] = overflow[overflowIndex++];
		}

		return resolvedSlots;
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
