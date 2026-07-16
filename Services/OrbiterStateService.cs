using Orbit.Models;
using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Orbit.Services;

/// <summary>
/// Owns the presentation-neutral state transitions for the Orbiter.
///
/// This service intentionally contains no WPF controls, Popup placement, command
/// registration, or domain-specific session logic. It provides a deterministic
/// seam that Classic and Spatial presentations can bind to while existing
/// floating-menu geometry and visibility services continue to own placement.
/// </summary>
public sealed class OrbiterStateService : INotifyPropertyChanged
{
	private OrbiterMode mode = OrbiterMode.Dormant;
	private OrbiterContextSnapshot? context;
	private string query = string.Empty;

	public event PropertyChangedEventHandler? PropertyChanged;

	/// <summary>
	/// Gets the currently active Orbiter presentation mode.
	/// </summary>
	public OrbiterMode Mode => mode;

	/// <summary>
	/// Gets the currently selected context, if any. A context may be retained while
	/// the Orbiter is dormant so summoning it can restore contextual commands.
	/// </summary>
	public OrbiterContextSnapshot? Context => context;

	/// <summary>
	/// Gets the current Command Lens query.
	/// </summary>
	public string Query => query;

	public bool IsVisible => mode != OrbiterMode.Dormant;
	public bool IsDormant => mode == OrbiterMode.Dormant;
	public bool IsGlobalNavigation => mode == OrbiterMode.GlobalNavigation;
	public bool IsCommandLens => mode == OrbiterMode.CommandLens;
	public bool IsContextMode => mode == OrbiterMode.Context;
	public bool HasContext => context != null;

	/// <summary>
	/// Summons the Orbiter. A retained selection context takes precedence over the
	/// global menu; otherwise Global Navigation is shown.
	/// </summary>
	public void Summon()
	{
		SetMode(context == null ? OrbiterMode.GlobalNavigation : OrbiterMode.Context);
	}

	/// <summary>
	/// Explicitly displays application-wide navigation without discarding a retained
	/// selection context. Use <see cref="ClearContext"/> when the selection itself is
	/// no longer valid.
	/// </summary>
	public void ShowGlobalNavigation()
	{
		SetQuery(string.Empty);
		SetMode(OrbiterMode.GlobalNavigation);
	}

	/// <summary>
	/// Enters Command Lens mode with an optional initial query.
	/// </summary>
	public void BeginCommandLens(string? initialQuery = null)
	{
		SetQuery(initialQuery ?? string.Empty);
		SetMode(OrbiterMode.CommandLens);
	}

	/// <summary>
	/// Updates the active Command Lens query. Query text may be staged before the
	/// visual lens is rendered so typing can summon the Orbiter.
	/// </summary>
	public void UpdateQuery(string? value)
	{
		SetQuery(value ?? string.Empty);
	}

	/// <summary>
	/// Sets the current selection context and optionally activates Context mode.
	/// </summary>
	public void ActivateContext(OrbiterContextSnapshot value, bool showContext = true)
	{
		ArgumentNullException.ThrowIfNull(value);
		if (string.IsNullOrWhiteSpace(value.Kind))
		{
			throw new ArgumentException("Orbiter context kind cannot be empty.", nameof(value));
		}

		if (string.IsNullOrWhiteSpace(value.DisplayName))
		{
			throw new ArgumentException("Orbiter context display name cannot be empty.", nameof(value));
		}

		SetContext(value);
		SetQuery(string.Empty);
		if (showContext)
		{
			SetMode(OrbiterMode.Context);
		}
	}

	/// <summary>
	/// Dismisses the expanded surface while retaining the current selection context.
	/// </summary>
	public void Dismiss()
	{
		SetQuery(string.Empty);
		SetMode(OrbiterMode.Dormant);
	}

	/// <summary>
	/// Clears the selected context and returns to Global Navigation or Dormant mode.
	/// </summary>
	public void ClearContext(bool showGlobalNavigation = true)
	{
		SetContext(null);
		SetQuery(string.Empty);
		SetMode(showGlobalNavigation ? OrbiterMode.GlobalNavigation : OrbiterMode.Dormant);
	}

	/// <summary>
	/// Applies the Spatial back contract.
	///
	/// Command Lens returns to Context when a valid context exists, otherwise Global
	/// Navigation. Context clears the selection and returns to Global Navigation.
	/// Global Navigation dismisses to Dormant. Dormant has no back transition.
	/// </summary>
	/// <returns><see langword="true"/> when a transition was handled.</returns>
	public bool NavigateBack()
	{
		switch (mode)
		{
			case OrbiterMode.CommandLens:
				SetQuery(string.Empty);
				SetMode(context == null ? OrbiterMode.GlobalNavigation : OrbiterMode.Context);
				return true;

			case OrbiterMode.Context:
				ClearContext(showGlobalNavigation: true);
				return true;

			case OrbiterMode.GlobalNavigation:
				Dismiss();
				return true;

			case OrbiterMode.Dormant:
			default:
				return false;
		}
	}

	private void SetMode(OrbiterMode value)
	{
		if (mode == value)
		{
			return;
		}

		mode = value;
		OnPropertyChanged(nameof(Mode));
		OnPropertyChanged(nameof(IsVisible));
		OnPropertyChanged(nameof(IsDormant));
		OnPropertyChanged(nameof(IsGlobalNavigation));
		OnPropertyChanged(nameof(IsCommandLens));
		OnPropertyChanged(nameof(IsContextMode));
	}

	private void SetContext(OrbiterContextSnapshot? value)
	{
		if (Equals(context, value))
		{
			return;
		}

		context = value;
		OnPropertyChanged(nameof(Context));
		OnPropertyChanged(nameof(HasContext));
	}

	private void SetQuery(string value)
	{
		if (string.Equals(query, value, StringComparison.Ordinal))
		{
			return;
		}

		query = value;
		OnPropertyChanged(nameof(Query));
	}

	private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
		=> PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
