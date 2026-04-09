using System;
using Avalonia.Controls.UnitTests.Utils;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Rendering;
using Avalonia.UnitTests;
using Avalonia.VisualTree;
using Moq;
using Xunit;

namespace Avalonia.Controls.Primitives.UnitTests
{
    public class ToggleButtonTests : ScopedTestBase
    {
        private const string uncheckedClass = ":unchecked";
        private const string checkedClass = ":checked";
        private const string indeterminateClass = ":indeterminate";

        [Theory]
        [InlineData(false, uncheckedClass, false)]
        [InlineData(false, uncheckedClass, true)]
        [InlineData(true, checkedClass, false)]
        [InlineData(true, checkedClass, true)]
        [InlineData(null, indeterminateClass, false)]
        [InlineData(null, indeterminateClass, true)]
        public void ToggleButton_Has_Correct_Class_According_To_Is_Checked(bool? isChecked, string expectedClass, bool isThreeState)
        {
            var toggleButton = new ToggleButton();
            toggleButton.IsThreeState = isThreeState;
            toggleButton.IsChecked = isChecked;

            Assert.Contains(expectedClass, toggleButton.Classes);
        }

        [Fact]
        public void ToggleButton_Is_Checked_Binds_To_Bool()
        {
            var toggleButton = new ToggleButton();
            var source = new Class1();

            toggleButton.DataContext = source;
            toggleButton.Bind(ToggleButton.IsCheckedProperty, new Binding("Foo"));

            source.Foo = true;
            Assert.True(toggleButton.IsChecked);

            source.Foo = false;
            Assert.False(toggleButton.IsChecked);
        }

        [Fact]
        public void ToggleButton_ThreeState_Checked_Binds_To_Nullable_Bool()
        {
            var threeStateButton = new ToggleButton();
            var source = new Class1();

            threeStateButton.DataContext = source;
            threeStateButton.Bind(ToggleButton.IsCheckedProperty, new Binding(nameof(Class1.NullableFoo)));

            source.NullableFoo = true;
            Assert.True(threeStateButton.IsChecked);

            source.NullableFoo = false;
            Assert.False(threeStateButton.IsChecked);

            source.NullableFoo = null;
            Assert.Null(threeStateButton.IsChecked);
        }

        [Fact]
        public void ToggleButton_IsCheckedChanged_Is_Raised_On_Is_Checked_Changes()
        {
            var threeStateButton = new ToggleButton();
            Assert.False(threeStateButton.IsChecked);

            var changeCount = 0;
            threeStateButton.IsCheckedChanged += (_, _) => ++changeCount;

            threeStateButton.IsChecked = true;
            Assert.Equal(1, changeCount);
            Assert.True(threeStateButton.IsChecked);

            threeStateButton.IsChecked = false;
            Assert.Equal(2, changeCount);
            Assert.False(threeStateButton.IsChecked);

            threeStateButton.IsChecked = null;
            Assert.Equal(3, changeCount);
            Assert.Null(threeStateButton.IsChecked);
        }

        [Fact]
        public void ToggleButton_IsCheckedChanged_Is_Raised_When_Toggling()
        {
            var threeStateButton = new TestToggleButton { IsThreeState = true };
            Assert.False(threeStateButton.IsChecked);

            var changeCount = 0;
            threeStateButton.IsCheckedChanged += (_, _) => ++changeCount;

            threeStateButton.Toggle();
            Assert.Equal(1, changeCount);
            Assert.True(threeStateButton.IsChecked);

            threeStateButton.Toggle();
            Assert.Equal(2, changeCount);
            Assert.Null(threeStateButton.IsChecked);

            threeStateButton.Toggle();
            Assert.Equal(3, changeCount);
            Assert.False(threeStateButton.IsChecked);
        }

        /// <summary>
        /// Regression test: ToggleButton.OnClick() calls Toggle() before base.OnClick(),
        /// so Toggle() runs even when the button is disabled (IsEffectivelyEnabled=false).
        /// This exposes the ordering bug directly, without needing the full pointer pipeline.
        /// </summary>
        [Fact]
        public void ToggleButton_Does_Not_Toggle_When_OnClick_Called_While_Disabled()
        {
            var command = new TestCommand(false); // command disabled from the start
            var button = new TestToggleButton { Command = command };
            var root = new TestRoot { Child = button };

            Assert.False(button.IsChecked);
            Assert.False(button.IsEffectivelyEnabled);

            // OnClick() can be reached via OnPointerReleased (no IsEffectivelyEnabled guard there).
            // ToggleButton.OnClick() calls Toggle() BEFORE base.OnClick() which has the guard,
            // so Toggle() always runs — this is the bug.
            button.SimulateClick();

            Assert.False(button.IsChecked); // FAILS without the fix
        }

        /// <summary>
        /// Regression test for the race-condition scenario described in the PR:
        /// user presses the button while the command is enabled, the command becomes
        /// disabled before pointer release, but the toggle still fires on release.
        /// </summary>
        [Fact]
        public void ToggleButton_Does_Not_Toggle_When_Command_Disabled_Between_Press_And_Release()
        {
            // Mock the hit-tester so GetVisualsAt returns the button when the pointer is over it.
            var renderer = new Mock<IHitTester>();
            renderer.Setup(r => r.HitTest(
                    It.IsAny<Point>(),
                    It.IsAny<Visual>(),
                    It.IsAny<Func<Visual, bool>>()))
                .Returns<Point, Visual, Func<Visual, bool>>((p, r, f) =>
                    r.Bounds.Contains(p) ? new Visual[] { r } : Array.Empty<Visual>());

            using var _ = UnitTestApplication.Start(TestServices.StyledWindow);

            var command = new TestCommand(true); // starts enabled
            var window = new Window { HitTesterOverride = renderer.Object };
            var button = new ToggleButton
            {
                Width = 100,
                Height = 100,
                VerticalAlignment = VerticalAlignment.Top,
                HorizontalAlignment = HorizontalAlignment.Left,
                Command = command,
            };
            window.Content = button;
            window.Show();

            var pt = new Point(50, 50);
            var helper = new MouseTestHelper();

            // 1. User presses the button while the command can execute.
            helper.Down(button, MouseButton.Left, pt);
            Assert.True(button.IsPressed);
            Assert.False(button.IsChecked);

            // 2. Command becomes disabled before the pointer is released.
            //    (e.g. another part of the UI fires a state change)
            command.IsEnabled = false;
            Assert.False(button.IsEffectivelyEnabled);
            // IsPressed is still true — disabling the command does NOT release pointer capture.
            Assert.True(button.IsPressed);

            // 3. User releases the pointer.
            //    Button.OnPointerReleased has no IsEffectivelyEnabled guard, so it calls OnClick().
            //    ToggleButton.OnClick() then calls Toggle() before base.OnClick()'s guard — bug!
            helper.Up(button, MouseButton.Left, pt);

            // Expected: IsChecked stays false (the click should be rejected because the
            //           button is disabled). Without the fix this assertion fails.
            Assert.False(button.IsChecked);
        }

        private class Class1 : NotifyingBase
        {
            private bool _foo;
            private bool? nullableFoo;

            public bool Foo
            {
                get { return _foo; }
                set { _foo = value; RaisePropertyChanged(); }
            }

            public bool? NullableFoo
            {
                get { return nullableFoo; }
                set { nullableFoo = value; RaisePropertyChanged(); }
            }
        }

        private class TestToggleButton : ToggleButton
        {
            public new void Toggle() => base.Toggle();
            public void SimulateClick() => OnClick();
        }
    }
}
