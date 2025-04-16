// ---------------------------------------------------------------------------------------
// Artemis - Coverage Analyser system for Flux
// Copyright (c) Metamation India.                                                ===  --
// ---------------------------------------------------------------------------      ==(  )
// RelayCommand.cs                                                                ===  --
// ---------------------------------------------------------------------------------------
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
namespace BookTool;

#region Class RelayCommand ------------------------------------------------------------------------
/// <summary>Implements ICommand interface for InputBinding</summary>
public class RelayCommand : ICommand {
   #region Constructor -----------------------------------------------
   public RelayCommand (Action execute, Func<bool> canExecute)
      => (mExecute, mCanExecute) = (execute, canExecute);
   readonly Action mExecute;
   readonly Func<bool> mCanExecute;
   #endregion

   #region Interface ------------------------------------------------
   event EventHandler? ICommand.CanExecuteChanged {
      add { CommandManager.RequerySuggested += value; }
      remove { CommandManager.RequerySuggested -= value; }
   }

   public bool CanExecute (object? parameter)
      => mCanExecute ();

   public void Execute (object? parameter)
      => mExecute ();
   #endregion
}
#endregion

#region Class RunLink -----------------------------------------------------------------------------
public class RunLink : Run {
   #region Constructors ---------------------------------------------
   public RunLink (string content) : base (content) {
      MouseEnter += OnHyperlinkMouseEnter;
      MouseLeave += OnHyperlinkMouseLeave;
   }

   public RunLink () : base () {
      MouseEnter += OnHyperlinkMouseEnter;
      MouseLeave += OnHyperlinkMouseLeave;
   }
   #endregion

   #region Runlink Events -------------------------------------------
   void OnHyperlinkMouseLeave (object sender, MouseEventArgs e) {
      if (sender is Run run) run.Foreground = mPrev;
   }
   Brush mPrev;

   void OnHyperlinkMouseEnter (object sender, MouseEventArgs e) {
      if (sender is Run run) {
         mPrev = run.Foreground;
         run.Foreground = Brushes.Blue;
      }
   }
   #endregion
}
#endregion