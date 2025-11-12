using System;
using System.Text.Json.Serialization;

namespace Orbit.Models
{
	public class AccountModel : ObservableObject
	{
		private string _username = string.Empty;
		private string _password = string.Empty;
		private int _preferredWorld = 1;
		private DateTime _lastUsed = DateTime.MinValue;
		private bool _autoLogin;
		private string _notes = string.Empty;
		private string _nickname = string.Empty;

		[JsonPropertyName("username")]
		public string Username
		{
			get => _username;
			set => SetProperty(ref _username, value);
		}

		[JsonPropertyName("password")]
		public string Password
		{
			get => _password;
			set => SetProperty(ref _password, value);
		}

		[JsonPropertyName("preferredWorld")]
		public int PreferredWorld
		{
			get => _preferredWorld;
			set => SetProperty(ref _preferredWorld, value);
		}

		[JsonPropertyName("lastUsed")]
		public DateTime LastUsed
		{
			get => _lastUsed;
			set => SetProperty(ref _lastUsed, value);
		}

		[JsonPropertyName("autoLogin")]
		public bool AutoLogin
		{
			get => _autoLogin;
			set => SetProperty(ref _autoLogin, value);
		}

		[JsonPropertyName("notes")]
		public string Notes
		{
			get => _notes;
			set => SetProperty(ref _notes, value);
		}

		[JsonPropertyName("nickname")]
		public string Nickname
		{
			get => _nickname;
			set
			{
				var normalized = value ?? string.Empty;
				SetProperty(ref _nickname, normalized);
			}
		}

		[JsonIgnore]
		public string PasswordMasked => new string('●', Math.Max(8, Password.Length));

		[JsonIgnore]
		public string LastUsedDisplay =>
			LastUsed == DateTime.MinValue ? "Never" : LastUsed.ToString("g");
	}
}
