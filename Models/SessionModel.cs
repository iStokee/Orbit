using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Orbit.Views;

namespace Orbit.Models
{
	public class SessionModel : INotifyPropertyChanged
	{
		private SessionState _state;
		private InjectionState _injectionState;
		private string _lastError;
		private Process _rsProcess;
		private nint _externalHandle;

		public SessionModel()
		{
			State = SessionState.Initializing;
			InjectionState = InjectionState.NotReady;
		}

		public Guid Id { get; init; }

		private string name;
		public string Name
		{
			get => name;
			set
			{
				if (name == value)
					return;
				name = value;
				OnPropertyChanged();
			}
		}

		public DateTime CreatedAt { get; init; }
		public ChildClientView HostControl { get; init; }
		public RSForm RSForm { get; set; }

		public SessionState State
		{
			get => _state;
			private set
			{
				if (_state == value)
					return;

				_state = value;
				OnPropertyChanged();
				OnPropertyChanged(nameof(StatusSummary));
				OnPropertyChanged(nameof(IsInjectable));
				OnPropertyChanged(nameof(IsHealthy));
			}
		}

		public InjectionState InjectionState
		{
			get => _injectionState;
			private set
			{
				if (_injectionState == value)
					return;

				_injectionState = value;
				OnPropertyChanged();
				OnPropertyChanged(nameof(StatusSummary));
				OnPropertyChanged(nameof(IsInjectable));
				OnPropertyChanged(nameof(IsHealthy));
			}
		}

		public string LastError
		{
			get => _lastError;
			private set
			{
				if (_lastError == value)
					return;

				_lastError = value;
				OnPropertyChanged();
			}
		}

		public Process RSProcess
		{
			get => _rsProcess;
			set
			{
				if (_rsProcess == value)
					return;

				_rsProcess = value;
				OnPropertyChanged();
			}
		}

		public nint ExternalHandle
		{
			get => _externalHandle;
			set
			{
				if (_externalHandle == value)
					return;

				_externalHandle = value;
				OnPropertyChanged();
			}
		}

		public string StatusSummary => $"{State} / {InjectionState}";

		public bool IsInjectable => InjectionState == InjectionState.Ready || InjectionState == InjectionState.Failed;
		public bool IsHealthy => State != SessionState.Failed && InjectionState != InjectionState.Failed;

		public event PropertyChangedEventHandler PropertyChanged;

		public void UpdateState(SessionState state, bool clearError = true)
		{
			if (clearError)
			{
				LastError = null;
			}
			State = state;
		}

		public void UpdateInjectionState(InjectionState state)
			=> InjectionState = state;

		public void Fail(Exception exception)
		{
			LastError = exception?.Message ?? "Unknown error";
			State = SessionState.Failed;
		}

		public void RecordInjectionFailure(Exception exception)
		{
			LastError = exception?.Message ?? "Unknown error";
			InjectionState = InjectionState.Failed;
			UpdateState(SessionState.ClientReady, clearError: false);
		}

		public void KillProcess()
		{
			try
			{
				if (RSProcess != null && !RSProcess.HasExited)
				{
					RSProcess.Kill();
					RSProcess.Dispose();
				}
			}
			catch
			{
				// Best-effort shutdown; swallow exceptions for now.
			}
		}

		[DllImport("user32.dll")]
		private static extern bool SetForegroundWindow(nint hWnd);

		public void SetFocus()
		{
			if (ExternalHandle != nint.Zero)
			{
				SetForegroundWindow(ExternalHandle);
			}
		}

		public override string ToString()
		{
			return string.IsNullOrWhiteSpace(Name)
				? base.ToString()
				: Name;
		}

		protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
			=> PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
	}
}
