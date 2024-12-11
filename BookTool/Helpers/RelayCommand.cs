// ---------------------------------------------------------------------------------------
// Artemis - Coverage Analyser system for Flux
// Copyright (c) Metamation India.                                                ===  --
// ---------------------------------------------------------------------------      ==(  )
// RelayCommand.cs                                                                ===  --
// ---------------------------------------------------------------------------------------
using System;
using System.Windows.Input;
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