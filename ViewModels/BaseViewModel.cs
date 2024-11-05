using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Orbit.ViewModels
{
	public abstract class BaseViewModel : INotifyPropertyChanged
	{
		public event PropertyChangedEventHandler PropertyChanged;

		/// <summary>
		/// Raises the PropertyChanged event for a specific property.
		/// </summary>
		/// <param name="propertyName">Name of the property that changed.</param>
		protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
		{
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
		}

		/// <summary>
		/// Sets the property value and raises the PropertyChanged event if the value changes.
		/// </summary>
		/// <typeparam name="T">Type of the property.</typeparam>
		/// <param name="field">The field storing the property's current value.</param>
		/// <param name="value">The new value to set.</param>
		/// <param name="propertyName">Name of the property, automatically set by the CallerMemberName attribute.</param>
		/// <returns>True if the value changed; otherwise, false.</returns>
		protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
		{
			if (Equals(field, value))
				return false;

			field = value;
			OnPropertyChanged(propertyName);
			return true;
		}
	}
}
