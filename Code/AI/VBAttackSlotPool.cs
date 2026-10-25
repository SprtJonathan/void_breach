using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>Scene-owned, expiring FIFO attack turns. Time is supplied by the host.</summary>
public sealed class VBAttackSlotPool<TTarget, TActor>
{
	private sealed class Request
	{
		public TTarget Target;
		public TActor Actor;
		public float RefreshedAt, GrantedUntil;
		public bool Granted;
	}
	private readonly List<Request> _requests = new();

	public bool RequestTurn( TTarget target, TActor actor, float now, int capacity, float duration, float grace )
	{
		// A target switch cannot retain a turn against the previous player.
		_requests.RemoveAll( r => EqualityComparer<TActor>.Default.Equals( r.Actor, actor )
			&& !EqualityComparer<TTarget>.Default.Equals( r.Target, target ) );
		_requests.RemoveAll( r => now - r.RefreshedAt > MathF.Max( 0.05f, grace ) );
		var request = _requests.FirstOrDefault( r => EqualityComparer<TActor>.Default.Equals( r.Actor, actor ) );
		if ( request is null )
		{
			request = new Request { Target = target, Actor = actor };
			_requests.Add( request );
		}
		request.RefreshedAt = now;
		// Move expired holders behind waiting actors, independent of update order.
		foreach ( var expired in _requests.Where( r => r.Granted && r.GrantedUntil <= now ).ToArray() )
		{
			expired.Granted = false;
			_requests.Remove( expired );
			_requests.Add( expired );
		}
		int occupied = 0;
		foreach ( var entry in _requests.Where( r => EqualityComparer<TTarget>.Default.Equals( r.Target, target ) && r.Granted ) )
			if ( ++occupied > Math.Max( 0, capacity ) ) entry.Granted = false;
		occupied = _requests.Count( r => EqualityComparer<TTarget>.Default.Equals( r.Target, target ) && r.Granted );
		foreach ( var waiting in _requests.Where( r => EqualityComparer<TTarget>.Default.Equals( r.Target, target ) && !r.Granted ) )
		{
			if ( occupied >= Math.Max( 0, capacity ) ) break;
			waiting.Granted = true;
			waiting.GrantedUntil = now + MathF.Max( 0.1f, duration );
			occupied++;
		}
		return request.Granted;
	}

	public void Release( TActor actor ) => _requests.RemoveAll( r => EqualityComparer<TActor>.Default.Equals( r.Actor, actor ) );
	public void RemoveInvalid( Func<TTarget, TActor, bool> valid ) => _requests.RemoveAll( r => !valid( r.Target, r.Actor ) );
}
