using System;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Sandbox.UI;

/// <summary>Instance-scoped colour overrides; authored layout and default styling stay untouched.</summary>
public sealed partial class VBHudPaletteStyles
{
	private readonly record struct Rule( string Selector, VBHudColorSlot Slot, string Declarations );
	private static readonly VBHudPalette Defaults = new();
	private static readonly Regex ColorLiteral = new( @"#[0-9a-fA-F]{6}\b|rgba?\([^)]*\)" );
	private Panel _panel;
	private StyleSheet _sheet;
	private int _hash;
	private bool _initialized;

	public void Apply( Panel panel, VBHudPalette palette, Guid id )
	{
		if ( panel is null ) return;
		palette ??= Defaults;
		int hash = palette.ColorHash();
		if ( _initialized && _panel == panel && _hash == hash ) return;
		if ( _panel is not null && _sheet is not null ) _panel.StyleSheet.Remove( _sheet );
		_panel = panel;
		_hash = hash;
		_initialized = true;
		_sheet = null;
		string css = Build( palette );
		if ( css.Length == 0 ) return;
		_sheet = StyleSheet.FromInline( css, $"vb-hud-palette-{id}" );
		panel.StyleSheet.Add( _sheet );
	}

	public static string Build( VBHudPalette palette )
	{
		if ( palette is null || palette.ColorHash() == Defaults.ColorHash() ) return string.Empty;
		var css = new StringBuilder();
		foreach ( var rule in Rules )
		{
			var baseline = Defaults.GetColor( rule.Slot );
			var selected = palette.GetColor( rule.Slot );
			// Keep every authored stage in the sheet, so restoring a stage also clears
			// any more general colour inherited from another customised stage.
			string declarations = selected == baseline ? rule.Declarations
				: ColorLiteral.Replace( rule.Declarations, match => Recolor( match.Value, baseline, selected ) );
			css.Append( rule.Selector ).Append( " { " ).Append( declarations ).AppendLine( " }" );
		}
		return css.ToString();
	}

	private static string Recolor( string literal, Color baseline, Color selected )
	{
		var parsed = Color.Parse( literal );
		if ( !parsed.HasValue ) return literal;
		var original = parsed.Value;
		float brightness = MathF.Max( original.r, MathF.Max( original.g, original.b ) );
		// Neutral dark panels and aim outlines keep their original contrast.
		if ( brightness < 0.16f ) return literal;
		float reference = MathF.Max( 0.001f, MathF.Max( baseline.r, MathF.Max( baseline.g, baseline.b ) ) );
		float weight = brightness / reference;
		float r = Math.Clamp( original.r + (selected.r - baseline.r) * weight, 0f, 1f );
		float g = Math.Clamp( original.g + (selected.g - baseline.g) * weight, 0f, 1f );
		float b = Math.Clamp( original.b + (selected.b - baseline.b) * weight, 0f, 1f );
		float a = Math.Clamp( original.a * selected.a, 0f, 1f );
		return $"rgba({Number( r * 255f )}, {Number( g * 255f )}, {Number( b * 255f )}, {Number( a )})";
	}

	private static string Number( float value ) => value.ToString( "0.####", CultureInfo.InvariantCulture );
}
