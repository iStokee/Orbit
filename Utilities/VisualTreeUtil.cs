using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace Orbit.Utilities;

/// <summary>
/// Shared WPF visual-tree traversal helpers. Previously duplicated verbatim across several
/// services/view-models; consolidated here so there is a single implementation.
/// </summary>
public static class VisualTreeUtil
{
	/// <summary>Depth-first enumeration of all descendant visuals of type <typeparamref name="T"/>.</summary>
	public static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent) where T : DependencyObject
	{
		if (parent == null)
		{
			yield break;
		}

		var count = VisualTreeHelper.GetChildrenCount(parent);
		for (var index = 0; index < count; index++)
		{
			var child = VisualTreeHelper.GetChild(parent, index);
			if (child is T typed)
			{
				yield return typed;
			}

			foreach (var descendant in FindVisualChildren<T>(child))
			{
				yield return descendant;
			}
		}
	}

	/// <summary>Walks up the visual tree to the nearest ancestor of type <typeparamref name="T"/>.</summary>
	public static T? FindVisualAncestor<T>(DependencyObject child) where T : DependencyObject
	{
		var current = child;
		while (current != null)
		{
			if (current is T typed)
			{
				return typed;
			}

			current = VisualTreeHelper.GetParent(current);
		}

		return null;
	}
}
