using System.ComponentModel;

namespace Orbit.Models
{
	public enum FloatingMenuQuickToggleMode
	{
		[Description("Middle mouse button")]
		MiddleMouse,

		[Description("Double right-click")]
		RightDoubleClick,

		[Description("Ctrl + left-click")]
		CtrlLeftClick,

		[Description("Home key")]
		HomeKey,

		[Description("End key")]
		EndKey
	}
}
