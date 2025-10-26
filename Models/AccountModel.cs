using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace Orbit.Models
{
	public class AccountModel : INotifyPropertyChanged
	{
		private string _username = string.Empty;
		private string _password = string.Empty;
		private int _preferredWorld = 1;
		private DateTime _lastUsed = DateTime.MinValue;
		private bool _autoLogin;
		private string _notes = string.Empty;

		[JsonPropertyName("username")]
		public string Username
		{
			get => _username;
			set
			{
				if (_username == value) return;
				_username = value;
				OnPropertyChanged();
			}
		}

		[JsonPropertyName("password")]
		public string Password
		{
			get => _password;
			set
			{
				if (_password == value) return;
				_password = value;
				OnPropertyChanged();
			}
		}

		[JsonPropertyName("preferredWorld")]
		public int PreferredWorld
		{
			get => _preferredWorld;
			set
			{
				if (_preferredWorld == value) return;
				_preferredWorld = value;
				OnPropertyChanged();
			}
		}

		[JsonPropertyName("lastUsed")]
		public DateTime LastUsed
		{
			get => _lastUsed;
			set
			{
				if (_lastUsed == value) return;
				_lastUsed = value;
				OnPropertyChanged();
			}
		}

		[JsonPropertyName("autoLogin")]
		public bool AutoLogin
		{
			get => _autoLogin;
			set
			{
				if (_autoLogin == value) return;
				_autoLogin = value;
				OnPropertyChanged();
			}
		}

		[JsonPropertyName("notes")]
		public string Notes
		{
			get => _notes;
			set
			{
				if (_notes == value) return;
				_notes = value;
				OnPropertyChanged();
			}
		}

		[JsonIgnore]
		public string PasswordMasked => new string('●', Math.Max(8, Password.Length));

		[JsonIgnore]
		public string LastUsedDisplay =>
			LastUsed == DateTime.MinValue ? "Never" : LastUsed.ToString("g");

		public event PropertyChangedEventHandler? PropertyChanged;

		protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
		{
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
		}
	}
}
