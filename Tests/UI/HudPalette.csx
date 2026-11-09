// Native inspector/serialization/CSS regression. Host Play with an existing local HUD.
var scene = Game.ActiveScene;
if ( !Game.IsPlaying || scene is null ) throw new Exception( "Requires Play" );
var hud = scene.GetAll<PanelComponent>().OfType<Sandbox.UI.VBMasterHud>().Single();
var original = hud.Colors;
var checks = new System.Collections.Generic.List<string>();
void Check( bool pass, string label ) { if ( !pass ) throw new Exception( label ); checks.Add( label ); }
Color Hex( string hex ) => Color.Parse( hex ).Value;
bool Near( Color? actual, Color expected ) => actual.HasValue
	&& MathF.Abs( actual.Value.r - expected.r ) < 0.006f && MathF.Abs( actual.Value.g - expected.g ) < 0.006f
	&& MathF.Abs( actual.Value.b - expected.b ) < 0.006f && MathF.Abs( actual.Value.a - expected.a ) < 0.006f;
var fixture = hud.Panel.AddChild<Sandbox.UI.Panel>();
fixture.Style.Set( "position", "absolute" ); fixture.Style.Left = -20000; fixture.Style.Top = -20000;
Sandbox.UI.Panel Node( Sandbox.UI.Panel parent, string tag, string classes )
{
	var p = parent.AddChild<Sandbox.UI.Panel>(); p.ElementName = tag.ToLowerInvariant();
	foreach ( var name in classes.Split( ' ', StringSplitOptions.RemoveEmptyEntries ) ) p.AddClass( name );
	return p;
}
void Capture( string label )
{
	using var bitmap = new Bitmap( 1280, 720 ); scene.Camera.RenderToBitmap( bitmap, true );
	System.IO.File.WriteAllBytes( System.IO.Path.Combine( exchangeDirectory, $"hud-palette-{label}.png" ), bitmap.ToPng() );
}
try
{
	hud.Colors = new VBHudPalette();
	Check( VBHudPaletteStyles.Build( hud.Colors ).Length == 0, "Default palette adds no stylesheet and preserves all authored colours" );
	var serialized = hud.GetSerialized().GetProperty( "Colors" );
	Check( serialized is not null && serialized.GetValue<VBHudPalette>() == hud.Colors, "HUD Colors is an editable serialized component property" );
	var widget = Editor.ControlWidget.Create( serialized );
	try { Check( widget is not null && !widget.GetType().Name.Contains( "Invalid" ), $"Native inspector creates a palette editor ({widget?.GetType().Name})" ); }
	finally { widget?.Destroy(); }
	Check( hud.Colors.GetSerialized().GetProperty( "Health" ).GetValue<VBHudHealthColors>().Normal == Hex( "#ffd36f" ),
		"Expandable health settings expose the original normal colour" );
	await Task.Delay( 200, cancellationToken );
	var health = hud.Panel.Descendants.First( p => p.HasClass( "health-readout" ) );
	Check( Near( health.ComputedStyle.FontColor, Hex( "#ffd36f" ) ), "Rendered default health colour matches the original HUD" );
	Capture( "default" );
	var warning = Node( fixture, "div", "health-readout medium" );
	var critical = Node( fixture, "div", "health-readout danger" );
	var exhausted = Node( fixture, "div", "readout stamina-readout exhausted" );
	var ammo = Node( fixture, "VBAmmoHud", "empty" );
	var ammoValue = Node( ammo, "label", "loaded-ammo" );
	var crosshair = Node( fixture, "VBCrosshairHud", "empty" );
	var arm = Node( crosshair, "div", "arm" );
	var vehicle = Node( fixture, "VBVehicleHud", "" );
	var status = Node( vehicle, "div", "vehicle-status" );
	var turbo = Node( status, "label", "turbo" );
	var inventory = Node( fixture, "VBWeaponSelectorHud", "" );
	var active = Node( inventory, "div", "weapon-slot active" );
	var interactions = Node( fixture, "VBInteractionHud", "" );
	var inRange = Node( interactions, "div", "world-marker in-range" );
	var dot = Node( inRange, "div", "marker-dot" );
	var traversal = Node( fixture, "VBTraversalHud", "" );
	var traversalWarning = Node( traversal, "div", "traversal-card warning" );
	var deathRoot = Node( fixture, "VBMasterHud", "palette dead" );
	var deathTitle = Node( deathRoot, "label", "state-title" );

	hud.Colors.Health.Normal = Hex( "#40ff80" );
	hud.Colors.Health.Warning = Hex( "#4080ff" );
	hud.Colors.Health.Critical = Hex( "#ff40c0" );
	hud.Colors.Stamina.Exhausted = Hex( "#8040ff" );
	hud.Colors.Ammunition.Empty = Hex( "#40c0ff" );
	hud.Colors.Crosshair.Empty = Hex( "#ff40ff" );
	hud.Colors.Vehicle.Turbo = Hex( "#ffff40" );
	hud.Colors.Interaction.InRange = Hex( "#40ff40" );
	hud.Colors.Traversal.Warning = Hex( "#ff40ff" );
	hud.Colors.LifeState.Dead = Hex( "#40c0ff" );
	hud.Colors.Inventory.Active = Hex( "#40ff80" );
	await Task.Delay( 300, cancellationToken );
	Check( Near( health.ComputedStyle.FontColor, hud.Colors.Health.Normal ), "Changing normal recolours live health text" );
	Check( Near( warning.ComputedStyle.FontColor, hud.Colors.Health.Warning ), "Warning stage has an independent colour" );
	Check( Near( critical.ComputedStyle.FontColor, hud.Colors.Health.Critical ), "Critical stage has an independent colour" );
	Check( Near( exhausted.ComputedStyle.FontColor, hud.Colors.Stamina.Exhausted ), "Exhaustion can be configured separately from stamina critical" );
	Check( Near( ammoValue.ComputedStyle.FontColor, hud.Colors.Ammunition.Empty ), $"Empty ammunition uses its own configured colour (actual={ammoValue.ComputedStyle.FontColor}, expected={hud.Colors.Ammunition.Empty})" );
	Check( Near( arm.ComputedStyle.BackgroundColor, hud.Colors.Crosshair.Empty ), "Crosshair empty state uses its configured colour" );
	Check( Near( turbo.ComputedStyle.FontColor, hud.Colors.Vehicle.Turbo ), "Turbo accent is configurable" );
	Check( Near( traversalWarning.ComputedStyle.FontColor, hud.Colors.Traversal.Warning ), "Traversal warning is configurable" );
	Check( Near( deathTitle.ComputedStyle.FontColor, hud.Colors.LifeState.Dead ), "Death colour can differ from downed colour" );
	Check( dot.ComputedStyle.BackgroundColor?.g > 0.95f && dot.ComputedStyle.BackgroundColor?.r < 0.3f,
		"In-range interaction dot follows the in-range palette" );
	Check( active.ComputedStyle.BorderLeftColor?.g > 0.9f && active.ComputedStyle.BorderLeftColor?.r < 0.3f,
		"Inventory active outline follows the active palette" );
	Check( Near( health.ComputedStyle.BackgroundColor, Color.Parse( "rgba(9,12,15,0.39)" ).Value ),
		"Changing accent colours preserves dark panel contrast and alpha" );
	Capture( "custom" );

	var saved = Json.Serialize( hud.Colors );
	var restored = Json.Deserialize<VBHudPalette>( saved );
	Check( Near( restored.Health.Normal, hud.Colors.Health.Normal ) && Near( restored.Crosshair.Empty, hud.Colors.Crosshair.Empty ),
		"Nested element and stage colours survive serialization round-trip" );
	hud.Colors = restored;
	hud.Colors.UseMasterColors = true;
	hud.Colors.Master.Normal = Hex( "#40ffff" );
	hud.Colors.Master.Warning = Hex( "#ffff40" );
	hud.Colors.Master.Critical = Hex( "#ff40ff" );
	hud.Colors.Health.OverrideMaster = true;
	await Task.Delay( 250, cancellationToken );
	Check( Near( health.ComputedStyle.FontColor, restored.Health.Normal ), "Per-element override keeps health independent from the master palette" );
	Check( Near( ammoValue.ComputedStyle.FontColor, restored.Master.Critical ), "Master critical recolours ammunition stages" );
	Check( Near( arm.ComputedStyle.BackgroundColor, restored.Master.Critical ), "Master critical recolours crosshair stages" );
	hud.Colors.Health.OverrideMaster = false;
	await Task.Delay( 200, cancellationToken );
	Check( Near( health.ComputedStyle.FontColor, restored.Master.Normal ), "Disabling the override restores master inheritance" );
	hud.Colors.UseMasterColors = false;
	await Task.Delay( 200, cancellationToken );
	Check( Near( health.ComputedStyle.FontColor, restored.Health.Normal ), "Disabling master restores saved per-element colours" );
	hud.Colors.Health = null;
	Check( hud.Colors.GetColor( VBHudColorSlot.HealthNormal ) == Hex( "#ffd36f" ), "Missing nested settings safely fall back to current defaults" );
	hud.Colors = new VBHudPalette();
	await Task.Delay( 200, cancellationToken );
	Check( Near( health.ComputedStyle.FontColor, Hex( "#ffd36f" ) ), "Resetting the palette removes colour overrides immediately" );
	return new { passed = checks.Count, checks, captures = exchangeDirectory };
}
catch ( Exception ex ) { throw new Exception( Json.Serialize( new { error = ex.Message, checks } ) ); }
finally
{
	fixture.Delete();
	hud.Colors = original;
}
