// Compiled only into the temporary app copy by Build-ColumnChecks.ps1.
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using MedRecPlus.App.Views;
using MedRecPlus.Core.Data;
using MedRecPlus.Core.Models;
using MedRecPlus.Core.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Storage;

namespace MedRecPlus.App;

public sealed partial class MainWindow
{
    private readonly List<string> _columnChecks = [];
    private void CheckColumn(bool passed, string label) => _columnChecks.Add((passed ? "PASS " : "FAIL ") + label);

    private static IEnumerable<DependencyObject> ColumnNodes(DependencyObject root)
    {
        yield return root;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            foreach (var child in ColumnNodes(VisualTreeHelper.GetChild(root, index))) yield return child;
    }

    private static void ToggleColumn(CheckBox checkBox)
    {
        var peer = new CheckBoxAutomationPeer(checkBox);
        ((IToggleProvider)peer.GetPattern(PatternInterface.Toggle)).Toggle();
    }

    private static async Task WaitForColumnRows(Page page)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            if (page.IsLoaded && ColumnNodes(page).OfType<Grid>().Any(grid => grid.Name == "TableRow" && grid.IsLoaded)) return;
            await Task.Delay(50);
        }
        throw new TimeoutException("Table rows did not finish loading.");
    }

    private void CheckColumnLayout(Page page, int index, bool visible, string label)
    {
        page.UpdateLayout();
        var grids = ColumnNodes(page).OfType<Grid>().Where(grid => grid.IsLoaded && grid.Name is "TableRow" or "TableHeader").ToArray();
        foreach (var grid in grids.Where(grid => (grid.ColumnDefinitions[index].Width.Value > 0) != visible
            || grid.Children.OfType<FrameworkElement>().Where(cell => Grid.GetColumn(cell) == index)
                .Any(cell => (cell.Visibility == Visibility.Visible) != visible)))
            _columnChecks.Add($"TRACE {label}: grid={grid.Name}, loaded={grid.IsLoaded}, width={grid.ColumnDefinitions[index].Width}, cells="
                + string.Join("/", grid.Children.OfType<FrameworkElement>().Where(cell => Grid.GetColumn(cell) == index).Select(cell => cell.Visibility)));
        CheckColumn(grids.Length > 1 && grids.All(grid =>
            (grid.ColumnDefinitions[index].Width.Value > 0) == visible
            && grid.Children.OfType<FrameworkElement>().Where(cell => Grid.GetColumn(cell) == index)
                .All(cell => (cell.Visibility == Visibility.Visible) == visible)), label);
        CheckColumn(grids.All(grid => grid.Children.OfType<FrameworkElement>().Where(cell => Grid.GetColumn(cell) == 2
            || Grid.GetColumn(cell) == grid.ColumnDefinitions.Count - 1).All(cell => cell.Visibility == Visibility.Visible)),
            label + ": patient identity and actions remain visible");
    }

    private static IRangeValueProvider ColumnSizer(Grid header, string key)
    {
        var handle = header.Children.OfType<TableColumnResizeHandle>().Single(handle => Equals(handle.Tag, key));
        var peer = FrameworkElementAutomationPeer.CreatePeerForElement(handle);
        return (IRangeValueProvider)peer.GetPattern(PatternInterface.RangeValue);
    }

    private void CheckColumnWidth(Page page, int index, double expected, string label)
    {
        page.UpdateLayout();
        var grids = ColumnNodes(page).OfType<Grid>().Where(grid => grid.IsLoaded && grid.Name is "TableRow" or "TableHeader").ToArray();
        CheckColumn(grids.Length > 1 && grids.All(grid => Math.Abs(grid.ColumnDefinitions[index].ActualWidth - expected) < 1), label);
    }

    private static void SendColumnDrag(TableColumnResizeHandle handle, string eventName, object args)
    {
        // Exercise the same native event bridge used by the template's Thumb, without moving the user's pointer.
        var thumb = ColumnNodes(handle).OfType<Thumb>().Single();
        typeof(TableColumnResizeHandle).GetMethod(eventName, BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(handle, [thumb, args]);
    }

    public async void RunColumnChecks(string sessionKey, bool pointerOnly = false)
    {
        var phase = "initial";
        try
        {
            AppData.BaseDir = Path.Combine(AppContext.BaseDirectory, "column-sample-data", sessionKey);
            AppData.EnsureCreated();
            var databasePath = Path.Combine(AppData.BaseDir, "columns.db");
            var reopen = File.Exists(databasePath);
            phase = pointerOnly ? "pointer" : reopen ? "reopen" : "initial";
            var database = new Database(databasePath, new string('e', 64));
            database.Initialize();
            AppSession.Start(database, "column checks");
            if (!reopen)
            {
                var operatorId = AppSession.Operators.Create("Sample clinician", "Doctor");
                for (var index = 0; index < 28; index++)
                {
                    var patientId = AppSession.Patients.Create(new Patient
                    {
                        Name = "Sample Patient " + index, RecordNumber = "COL-" + index, Age = 30,
                        Address = "Sample address", PhoneNumber = "08123456789", InitialDiagnosis = "Sample diagnosis",
                    });
                    var visitId = AppSession.Visits.CreateVisit(patientId, operatorId, [],
                        [new TreatmentItem { Name = "Consultation", PriceCents = 100_000 }],
                        DiscountTypes.None, 0, null, null, null, AppSession.Actor);
                    // Include a visit without an invoice so its collapsed chip binding is exercised.
                    if (index != 27) AppSession.Invoices.GenerateFromAppointment(visitId, AppSession.Actor);
                }
            }
            EnterShell();
            AppWindow.Resize(new Windows.Graphics.SizeInt32(1400, 900));
            if (pointerOnly || !reopen)
            {
                await CheckColumnPointerDrag(typeof(PatientsPage), "record_number");
                await CheckColumnPointerDrag(typeof(AppointmentsPage), "date");
                await CheckColumnPointerDrag(typeof(InvoicesPage), "number");
                if (pointerOnly) return;
            }
            foreach (var spec in new[]
            {
                (Type: typeof(PatientsPage), Key: "patients", List: "PatientList", Indices: new[] { 1, 4, 5, 6, 7 }),
                (Type: typeof(AppointmentsPage), Key: "appointments", List: "VisitList", Indices: new[] { 1, 3, 4, 5 }),
                (Type: typeof(InvoicesPage), Key: "invoices", List: "InvoiceList", Indices: new[] { 1, 3, 4, 5 }),
            })
            {
                NavigateTo(spec.Type);
                await Task.Delay(250);
                var page = (Page)ContentFrame.Content;
                await WaitForColumnRows(page);
                var button = (Button)page.FindName("ColumnsButton");
                var flyout = (Flyout)button.Flyout;
                var boxes = ((StackPanel)flyout.Content).Children.Cast<CheckBox>().ToArray();
                var header = (Grid)page.FindName("TableHeader");
                var firstWidth = 200d + spec.Key.Length;
                var nameWidth = 240d + spec.Key.Length * 2;
                var lastWidth = 160d + spec.Key.Length * 3;
                var firstKey = (string)boxes[0].Tag;
                var lastKey = (string)boxes[^1].Tag;
                var handles = header.Children.OfType<TableColumnResizeHandle>().ToArray();
                CheckColumn(handles.Length == (spec.Type == typeof(PatientsPage) ? 7 : 5)
                    && handles.Where(handle => handle.Visibility == Visibility.Visible)
                        .All(handle => ColumnNodes(handle).OfType<Thumb>().Any() && handle.ActualWidth >= 10),
                    spec.Key + ": every data header has a native drag handle");
                CheckColumn(boxes.Length == spec.Indices.Length, spec.Key + ": all optional columns offered");
                if (reopen)
                {
                    CheckColumn(boxes.Select((box, index) => box.IsChecked == (index != 0 && index != boxes.Length - 1)).All(value => value),
                        spec.Key + ": picker state survives a full process restart");
                    for (var index = 0; index < boxes.Length; index++)
                        CheckColumnLayout(page, spec.Indices[index], boxes[index].IsChecked == true, spec.Key + ": restored column " + index);
                    CheckColumnWidth(page, 2, nameWidth, spec.Key + ": primary column width survives a full process restart");
                    ToggleColumn(boxes[0]);
                    ToggleColumn(boxes[^1]);
                    CheckColumnWidth(page, spec.Indices[0], firstWidth, spec.Key + ": hidden column width survives restart");
                    CheckColumnWidth(page, spec.Indices[^1], lastWidth, spec.Key + ": second hidden column width survives restart");
                    continue;
                }
                CheckColumn(boxes.All(box => box.IsChecked == true), spec.Key + ": all columns visible by default");
                var list = (ListView)page.FindName(spec.List);
                var firstItem = list.Items[0];
                firstItem.GetType().GetProperty("IsSelected")!.SetValue(firstItem, true);
                var firstSizer = ColumnSizer(header, firstKey);
                var dragHandle = handles.Single(handle => Equals(handle.Tag, firstKey));
                var originalWidth = firstSizer.Value;
                SendColumnDrag(dragHandle, "NativeDragStarted", new DragStartedEventArgs(0, 0));
                SendColumnDrag(dragHandle, "NativeDragDelta", new DragDeltaEventArgs(40, 0));
                SendColumnDrag(dragHandle, "NativeDragDelta", new DragDeltaEventArgs(12, 0));
                CheckColumnWidth(page, spec.Indices[0], originalWidth + 52, spec.Key + ": drag deltas resize the column live");
                SendColumnDrag(dragHandle, "NativeDragCompleted", new DragCompletedEventArgs(52, 0, false));
                var saved = AppSession.Settings.Get<Dictionary<string, double>>("table_column_widths." + spec.Key)!;
                CheckColumn(Math.Abs(saved[firstKey] - originalWidth - 52) < 1, spec.Key + ": finishing a drag saves the new width");
                SendColumnDrag(dragHandle, "NativeDragStarted", new DragStartedEventArgs(0, 0));
                SendColumnDrag(dragHandle, "NativeDragDelta", new DragDeltaEventArgs(75, 0));
                SendColumnDrag(dragHandle, "NativeDragCompleted", new DragCompletedEventArgs(75, 0, true));
                CheckColumnWidth(page, spec.Indices[0], originalWidth + 52, spec.Key + ": cancelling a drag restores the previous width");
                firstSizer.SetValue(1200);
                CheckColumnWidth(page, spec.Indices[0], 1200, spec.Key + ": resizing updates the header and all rendered rows");
                var horizontal = (ScrollViewer)page.FindName("TableScroll");
                horizontal.ChangeView(180, null, null, true);
                await Task.Delay(150);
                CheckColumn(horizontal.ScrollableWidth > 0 && horizontal.HorizontalOffset > 0
                    && ColumnNodes(list).OfType<Grid>().Where(grid => grid.Name == "TableRow" && grid.IsLoaded)
                        .All(row => Math.Abs(row.TransformToVisual(page).TransformPoint(new Windows.Foundation.Point()).X
                            - header.TransformToVisual(page).TransformPoint(new Windows.Foundation.Point()).X) < 1),
                    spec.Key + ": wide columns scroll the header and rows together");
                firstSizer.SetValue(firstSizer.Minimum);
                CheckColumnWidth(page, spec.Indices[0], firstSizer.Minimum, spec.Key + ": minimum width keeps a column visible");
                firstSizer.SetValue(firstWidth);
                ColumnSizer(header, "patient_name").SetValue(nameWidth);
                ColumnSizer(header, lastKey).SetValue(lastWidth);
                CheckColumnWidth(page, 2, nameWidth, spec.Key + ": primary patient column can also be resized");
                var invalidRejected = false;
                try { firstSizer.SetValue(double.NaN); }
                catch (ArgumentOutOfRangeException) { invalidRejected = true; }
                CheckColumn(invalidRejected, spec.Key + ": invalid widths are rejected without changing the table");
                horizontal.ChangeView(0, null, null, true);
                flyout.ShowAt(button);
                await Task.Delay(150);
                for (var index = 0; index < boxes.Length; index++)
                {
                    ToggleColumn(boxes[index]);
                    CheckColumnLayout(page, spec.Indices[index], false, spec.Key + ": hide " + boxes[index].Content);
                }
                var renderedRows = ColumnNodes(page).OfType<Grid>().Where(grid => grid.Name == "TableRow" && grid.IsLoaded).ToArray();
                CheckColumn(renderedRows.Length > 0 && renderedRows.All(row => Enumerable.Range(0, header.ColumnDefinitions.Count)
                    .All(index => Math.Abs(row.ColumnDefinitions[index].ActualWidth - header.ColumnDefinitions[index].ActualWidth) < 1)),
                    spec.Key + ": header and rows keep identical column widths after hiding columns");
                CheckColumn(flyout.IsOpen,
                    spec.Key + ": checkbox picker stays open for multiple changes");
                CheckColumn(ReferenceEquals(firstItem, list.Items[0]) && (bool)firstItem.GetType().GetProperty("IsSelected")!.GetValue(firstItem)!,
                    spec.Key + ": toggling preserves rows and selection");
                for (var index = 0; index < boxes.Length; index++)
                {
                    ToggleColumn(boxes[index]);
                    CheckColumnLayout(page, spec.Indices[index], true, spec.Key + ": restore " + boxes[index].Content);
                }
                CheckColumnWidth(page, spec.Indices[0], firstWidth, spec.Key + ": hiding and showing preserves custom widths");
                CheckColumnWidth(page, spec.Indices[^1], lastWidth, spec.Key + ": each optional column retains its own width");
                ToggleColumn(boxes[0]);
                ToggleColumn(boxes[^1]);
                flyout.Hide();
                spec.Type.GetMethod("NextPage_Click", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(page, [null, new RoutedEventArgs()]);
                await Task.Delay(200);
                CheckColumnLayout(page, spec.Indices[0], false, spec.Key + ": new page rows retain hidden columns");
                CheckColumnWidth(page, 2, nameWidth, spec.Key + ": new page rows retain custom widths");
                var size = (ComboBox)page.FindName("PageSizeBox");
                size.SelectedIndex = 2;
                await Task.Delay(200);
                var scroll = ColumnNodes(list).OfType<ScrollViewer>().First();
                scroll.ChangeView(null, scroll.ScrollableHeight, null, true);
                await Task.Delay(150);
                CheckColumnLayout(page, spec.Indices[^1], false, spec.Key + ": recycled rows retain hidden columns");
                if (spec.Type == typeof(AppointmentsPage))
                {
                    // Show the invoice column again; a visit with no invoice must keep its chip collapsed.
                    ToggleColumn(boxes[^1]);
                    scroll.ChangeView(null, 0, null, true);
                    await Task.Delay(150);
                    var emptyChips = ColumnNodes(list).OfType<Grid>()
                        .Where(grid => grid.Name == "TableRow" && grid.DataContext is AppointmentsPage.VisitRow { ChipVisibility: Visibility.Collapsed })
                        .Select(grid => (Grid)grid.Children.OfType<FrameworkElement>().Single(cell => Grid.GetColumn(cell) == 5)).ToArray();
                    CheckColumn(emptyChips.Length > 0 && emptyChips.All(grid => grid.Visibility == Visibility.Visible
                        && grid.Children.OfType<Border>().Single().Visibility == Visibility.Collapsed),
                        "appointments: invoice chip binding survives hide/show");
                    ToggleColumn(boxes[^1]);
                }
                foreach (var theme in new[] { ElementTheme.Light, ElementTheme.Dark })
                {
                    ShellRoot.RequestedTheme = theme;
                    ShellRoot.Background = new SolidColorBrush(theme == ElementTheme.Light ? Microsoft.UI.Colors.White : Windows.UI.Color.FromArgb(255, 32, 32, 32));
                    await Task.Delay(100);
                    await SaveColumnVisual(page, "columns-" + spec.Key + "-" + theme + ".png");
                }
                flyout.ShowAt(button);
                await Task.Delay(150);
                await SaveColumnVisual((FrameworkElement)flyout.Content, "column-picker-" + spec.Key + ".png");
                flyout.Hide();
                NavigateTo(typeof(DashboardPage));
                NavigateTo(spec.Type);
                await Task.Delay(200);
                var restored = (Page)ContentFrame.Content;
                await WaitForColumnRows(restored);
                CheckColumnLayout(restored, spec.Indices[0], false, spec.Key + ": navigation restores saved state");
                CheckColumnLayout(restored, spec.Indices[^1], false, spec.Key + ": page preferences stay independent");
                CheckColumnWidth(restored, 2, nameWidth, spec.Key + ": navigation restores the saved primary column width");
            }
            if (!reopen)
            {
                AppSession.Settings.Set("table_columns.patients", new { Damaged = true });
                NavigateTo(typeof(PatientsPage));
                await Task.Delay(150);
                var page = (Page)ContentFrame.Content;
                var boxes = ((StackPanel)((Flyout)((Button)page.FindName("ColumnsButton")).Flyout).Content).Children.Cast<CheckBox>().ToArray();
                CheckColumn(boxes.All(box => box.IsChecked == true), "malformed preference safely falls back to visible columns");
                ToggleColumn(boxes[0]);
                ToggleColumn(boxes[^1]);
            }
        }
        catch (Exception exception) { CheckColumn(false, exception.ToString()); }
        finally
        {
            _columnChecks.Add(_columnChecks.Any(line => line.StartsWith("FAIL ")) ? "COLUMN_UI_RESULT=FAIL" : "COLUMN_UI_RESULT=PASS");
            File.WriteAllLines(Path.Combine(AppContext.BaseDirectory, "column-check-results-" + phase + ".txt"), _columnChecks);
            Close();
        }
    }

    private static async Task SaveColumnVisual(FrameworkElement element, string name)
    {
        var bitmap = new RenderTargetBitmap();
        await bitmap.RenderAsync(element);
        var pixels = await bitmap.GetPixelsAsync();
        var path = Path.Combine(AppContext.BaseDirectory, name);
        File.WriteAllBytes(path, []);
        var file = await StorageFile.GetFileFromPathAsync(path);
        using var stream = await file.OpenAsync(FileAccessMode.ReadWrite);
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied,
            (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96, pixels.ToArray());
        await encoder.FlushAsync();
    }

    private async Task CheckColumnPointerDrag(Type pageType, string columnKey)
    {
        NavigateTo(pageType);
        var page = (Page)ContentFrame.Content;
        await WaitForColumnRows(page);
        page.UpdateLayout();
        var header = (Grid)page.FindName("TableHeader");
        var handle = header.Children.OfType<TableColumnResizeHandle>().Single(handle => Equals(handle.Tag, columnKey));
        var index = Grid.GetColumn(handle);
        var before = header.ColumnDefinitions[index].ActualWidth;
        var started = 0;
        var deltas = 0;
        var completed = 0;
        var pressed = 0;
        var pointerDistance = 0d;
        handle.DragStarted += (_, _) => started++;
        handle.DragDelta += (_, _) => deltas++;
        handle.DragCompleted += (_, args) => { completed++; pointerDistance = args.HorizontalChange; };
        var thumb = ColumnNodes(handle).OfType<Thumb>().Single();
        thumb.AddHandler(UIElement.PointerPressedEvent, (PointerEventHandler)((_, args) =>
        {
            pressed++;
        }), true);
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var previousWindow = ColumnPointerInput.GetForegroundWindow();
        var presenter = (Microsoft.UI.Windowing.OverlappedPresenter)AppWindow.Presenter;
        var wasAlwaysOnTop = presenter.IsAlwaysOnTop;
        ColumnPointerInput.GetCursorPos(out var previousCursor);
        try
        {
            AppWindow.Show();
            presenter.IsAlwaysOnTop = true;
            Activate();
            ColumnPointerInput.SetForegroundWindow(hwnd);
            await Task.Delay(250);
            var location = handle.TransformToVisual(ShellRoot).TransformPoint(
                new Windows.Foundation.Point(handle.ActualWidth / 2, handle.ActualHeight / 2));
            var origin = new ColumnPointerInput.Point();
            ColumnPointerInput.ClientToScreen(hwnd, ref origin);
            var scale = ShellRoot.XamlRoot.RasterizationScale;
            var x = origin.X + (int)Math.Round(location.X * scale);
            var y = origin.Y + (int)Math.Round(location.Y * scale);
            await Task.Run(() =>
            {
                ColumnPointerInput.Move(x, y);
                Thread.Sleep(100);
                var pointerWindow = ColumnPointerInput.GetAncestor(ColumnPointerInput.WindowFromPoint(new ColumnPointerInput.Point { X = x, Y = y }), 2);
                if (pointerWindow != hwnd)
                    throw new InvalidOperationException("The sample app must be under the pointer before sending a drag.");
                ColumnPointerInput.GetCursorPos(out var actualStart);
                if (Math.Abs(actualStart.X - x) > 2 || Math.Abs(actualStart.Y - y) > 2)
                    throw new InvalidOperationException($"Mouse input did not reach the divider: expected {x},{y}, actual {actualStart.X},{actualStart.Y}.");
                try
                {
                    ColumnPointerInput.Button(2);
                    Thread.Sleep(50);
                    for (var step = 1; step <= 10; step++)
                    {
                        ColumnPointerInput.Move(x + (int)Math.Round(6 * step * scale), y);
                        Thread.Sleep(30);
                    }
                }
                finally { ColumnPointerInput.Button(4); }
            });
            await Task.Delay(150);
            page.UpdateLayout();
            var after = header.ColumnDefinitions[index].ActualWidth;
            // Use the movement Windows actually delivered, including any concurrent desktop input.
            var expected = Math.Clamp(before + pointerDistance, handle.MinimumWidth, TableColumnResizeHandle.MaximumWidth);
            CheckColumn(pressed == 1 && started == 1 && deltas > 0 && completed == 1
                && Math.Abs(after - before) > 4 && Math.Abs(after - expected) < 1,
                $"{pageType.Name}: actual mouse drag resizes {before:F1} to {after:F1} for {pointerDistance:F1}px pointer movement (presses={pressed}, starts={started}, moves={deltas}, releases={completed})");
            CheckColumnWidth(page, index, expected, pageType.Name + ": rows follow an actual mouse resize");
        }
        finally
        {
            await Task.Run(() =>
            {
                ColumnPointerInput.Button(4);
                ColumnPointerInput.Move(previousCursor.X, previousCursor.Y);
            });
            presenter.IsAlwaysOnTop = wasAlwaysOnTop;
            if (previousWindow != IntPtr.Zero && previousWindow != hwnd) ColumnPointerInput.SetForegroundWindow(previousWindow);
        }
    }

    private static class ColumnPointerInput
    {
        [StructLayout(LayoutKind.Sequential)] internal struct Point { public int X; public int Y; }
        [StructLayout(LayoutKind.Sequential)] private struct MouseInput
        {
            public int X; public int Y; public uint Data; public uint Flags; public uint Time; public UIntPtr Extra;
        }
        // The verification app is always x64; match the native INPUT union's offset and size explicitly.
        [StructLayout(LayoutKind.Explicit, Size = 40)] private struct Input
        {
            [FieldOffset(0)] public uint Type;
            [FieldOffset(8)] public MouseInput Mouse;
        }
        [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] internal static extern bool SetForegroundWindow(IntPtr hwnd);
        [DllImport("user32.dll")] internal static extern bool GetCursorPos(out Point point);
        [DllImport("user32.dll")] internal static extern bool ClientToScreen(IntPtr hwnd, ref Point point);
        [DllImport("user32.dll")] internal static extern IntPtr WindowFromPoint(Point point);
        [DllImport("user32.dll")] internal static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
        [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
        [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, Input[] inputs, int size);
        internal static void Move(int x, int y)
        {
            Send(new MouseInput
            {
                X = (int)Math.Round((x - GetSystemMetrics(76)) * 65535d / (GetSystemMetrics(78) - 1)),
                Y = (int)Math.Round((y - GetSystemMetrics(77)) * 65535d / (GetSystemMetrics(79) - 1)),
                Flags = 0x8000 | 0x4000 | 1,
            });
        }
        internal static void Button(uint flags) => Send(new MouseInput { Flags = flags });
        private static void Send(MouseInput mouse)
        {
            if (SendInput(1, [new Input { Mouse = mouse }], Marshal.SizeOf<Input>()) != 1)
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        }
    }
}
