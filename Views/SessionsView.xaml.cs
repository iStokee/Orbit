using MahApps.Metro.Controls;
using Orbit.Classes;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace Orbit.Views
{
	/// <summary>
	/// Interaction logic for SessionsWindow.xaml
	/// </summary>
	public partial class SessionsView : MetroWindow
	{
		public SessionsView(ObservableCollection<Session> sessions)
		{
			InitializeComponent();
			DataContext = sessions;
			//GenerateColumns();
		}

		private void GenerateColumns()
		{
			var grid = SessionsGrid;
			grid.Columns.Clear();

			// Use reflection to get properties of the Session class
			foreach (PropertyInfo property in typeof(Session).GetProperties())
			{
				DataGridTextColumn column = new DataGridTextColumn
				{
					Header = property.Name,
					Binding = new System.Windows.Data.Binding(property.Name)
				};

				grid.Columns.Add(column);
			}
		}
	}
}
