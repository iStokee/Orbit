using System.Windows;
using System.Windows.Controls;
using UserControl = System.Windows.Controls.UserControl;

namespace Orbit.Views.Controls
{
	public partial class FloatingMenuSnapPreview : UserControl
	{
		public static readonly DependencyProperty EdgeThicknessProperty =
			DependencyProperty.Register(
				nameof(EdgeThickness),
				typeof(double),
				typeof(FloatingMenuSnapPreview),
				new PropertyMetadata(80d));

		public static readonly DependencyProperty CornerSizeProperty =
			DependencyProperty.Register(
				nameof(CornerSize),
				typeof(double),
				typeof(FloatingMenuSnapPreview),
				new PropertyMetadata(120d));

		public static readonly DependencyProperty CornerRadiusProperty =
			DependencyProperty.Register(
				nameof(CornerRadius),
				typeof(CornerRadius),
				typeof(FloatingMenuSnapPreview),
				new PropertyMetadata(new CornerRadius(60d)));

		public static readonly DependencyProperty EdgeCoverageProperty =
			DependencyProperty.Register(
				nameof(EdgeCoverage),
				typeof(double),
				typeof(FloatingMenuSnapPreview),
				new PropertyMetadata(0.85d));

		public static readonly DependencyProperty EdgeOpacityProperty =
			DependencyProperty.Register(
				nameof(EdgeOpacity),
				typeof(double),
				typeof(FloatingMenuSnapPreview),
				new PropertyMetadata(0.32d));

		public static readonly DependencyProperty CornerHeightProperty =
			DependencyProperty.Register(
				nameof(CornerHeight),
				typeof(double),
				typeof(FloatingMenuSnapPreview),
				new PropertyMetadata(120d));

		public FloatingMenuSnapPreview()
		{
			InitializeComponent();
		}

		public double EdgeThickness
		{
			get => (double)GetValue(EdgeThicknessProperty);
			set => SetValue(EdgeThicknessProperty, value);
		}

		public double CornerSize
		{
			get => (double)GetValue(CornerSizeProperty);
			set => SetValue(CornerSizeProperty, value);
		}

		public CornerRadius CornerRadius
		{
			get => (CornerRadius)GetValue(CornerRadiusProperty);
			set => SetValue(CornerRadiusProperty, value);
		}

		public double EdgeCoverage
		{
			get => (double)GetValue(EdgeCoverageProperty);
			set => SetValue(EdgeCoverageProperty, value);
		}

		public double EdgeOpacity
		{
			get => (double)GetValue(EdgeOpacityProperty);
			set => SetValue(EdgeOpacityProperty, value);
		}

		public double CornerHeight
		{
			get => (double)GetValue(CornerHeightProperty);
			set => SetValue(CornerHeightProperty, value);
		}
	}
}
