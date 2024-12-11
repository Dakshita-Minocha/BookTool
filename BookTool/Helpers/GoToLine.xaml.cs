// ---------------------------------------------------------------------------------------
// Artemis - Coverage Analyser system for Flux
// Copyright (c) Metamation India.                                                ===  --
// ---------------------------------------------------------------------------      ==(  )
// GoToLine.cs                                                                    ===  --
// ---------------------------------------------------------------------------------------
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
namespace BookTool;

#region Class GoToLine ----------------------------------------------------------------------------
public partial class GoToLine : Window, INotifyPropertyChanged {
   #region Constructor -----------------------------------------------
   public GoToLine (int maxRows) {
      InitializeComponent ();
      Title = $"Go To Line ({1} - {maxRows - 1})";
      mTB = new TextBox () { AutoWordSelection = true, MinLines = 1, Height = 20, Margin = new Thickness (10) };
      mTB.KeyDown += OnKeyDown;
      var binding = new Binding (nameof (InputText)) { Source = this, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged };
      binding.ValidationRules.Add (mLR = new LineRule (maxRows));
      mTB.SetBinding (TextBox.TextProperty, binding);
      var EnterCommand = new RelayCommand (SetResult, IsInputValid);
      mTB.InputBindings.Add (new KeyBinding (EnterCommand, new KeyGesture (Key.Enter)));
      mGrid.SetValue (Grid.RowProperty, 0);
      mGrid.Children.Add (mTB);
      var btnOK = new Button () { Content = "OK", Height = 20, Width = 60, Margin = new Thickness (5) };
      btnOK.InputBindings.Add (new MouseBinding (EnterCommand, new MouseGesture (MouseAction.LeftClick)));
      mStack.Children.Add (btnOK);
      mStack.Children.Add (new Button () { Content = "Cancel", IsCancel = true, Height = 20, Width = 60, Margin = new Thickness (5) });
      mTB.Focus ();
   }
   readonly LineRule mLR;
   readonly TextBox mTB;
   #endregion

   #region Properties -----------------------------------------------
   public int LineNumber => int.Parse (mTB.Text);
   public string InputText { set { PropertyChanged?.Invoke (this, new PropertyChangedEventArgs (nameof (InputText))); } }
   #endregion

   #region Interface ------------------------------------------------
   public event PropertyChangedEventHandler? PropertyChanged;
   #endregion

   #region Event Handler --------------------------------------------
   void OnKeyDown (object sender, KeyEventArgs e) {
      if (e.Key is Key.Escape) Close ();
   }
   #endregion

   #region Implementation --------------------------------------------
   bool IsInputValid ()
      => mLR.Validate (mTB.Text, CultureInfo.CurrentCulture).IsValid;

   void SetResult ()
      => DialogResult = IsInputValid ();
   #endregion
}
#endregion

#region Class LineRule ----------------------------------------------------------------------------
public class LineRule : ValidationRule {
   public LineRule (int max) => mMax = max;
   readonly int mMax;
   public override ValidationResult Validate (object value, CultureInfo cultureInfo)
      => int.TryParse ((string)value, out var line) && line > 0 && line < mMax ? ValidationResult.ValidResult : new ValidationResult (false, "Invalid line number");
}
#endregion