using System;
using System.Threading.Tasks;
using System.Windows.Input;

public class RelayCommand : ICommand
{
	private readonly Action<object> _execute;
	private readonly Predicate<object> _canExecute;

	public RelayCommand(Action<object> execute) : this(execute, null) { }

	public RelayCommand(Action<object> execute, Predicate<object> canExecute)
	{
		_execute = execute ?? throw new ArgumentNullException(nameof(execute));
		_canExecute = canExecute;
	}

	public RelayCommand(Func<object, Task> executeAsync, Predicate<object> canExecute = null)
	{
		_execute = async (param) => await executeAsync(param);
		_canExecute = canExecute;
	}

	public bool CanExecute(object parameter) => _canExecute == null || _canExecute(parameter);

	public void Execute(object parameter) => _execute(parameter);

	public event EventHandler CanExecuteChanged
	{
		add { CommandManager.RequerySuggested += value; }
		remove { CommandManager.RequerySuggested -= value; }
	}
}
