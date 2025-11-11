using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using Orbit.Models;

namespace Orbit.ViewModels;

/// <summary>
/// ViewModel for the Session Close confirmation dialog
/// </summary>
public class SessionCloseDialogViewModel : INotifyPropertyChanged
{
	private readonly SessionModel _session;

	public SessionCloseDialogViewModel(SessionModel session)
	{
		_session = session ?? throw new ArgumentNullException(nameof(session));
	}

	public string SessionName => _session.Name ?? "Unknown Session";
	public string ProcessId => _session.RSProcess?.Id.ToString() ?? "N/A";
	public string SessionType => _session.SessionType.ToString();
	public string State => _session.State.ToString();
	public string InjectionState => _session.InjectionState.ToString();
	public DateTime CreatedAt => _session.CreatedAt;

	public string TimeRunning
	{
		get
		{
			var elapsed = DateTime.Now - CreatedAt;
			if (elapsed.TotalHours >= 1)
				return $"{(int)elapsed.TotalHours}h {elapsed.Minutes}m";
			else if (elapsed.TotalMinutes >= 1)
				return $"{(int)elapsed.TotalMinutes}m {elapsed.Seconds}s";
			else
				return $"{(int)elapsed.TotalSeconds}s";
		}
	}

	public string StatusSummary => _session.StatusSummary;

	public event PropertyChangedEventHandler? PropertyChanged;

	protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
	{
		PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
	}
}
