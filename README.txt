BookTool
README

Booktool helps to organise all changes made to a documentation repository after a certain date into a single file.

Pre-requisites:
   - Set global .gitconfig
   - Download winmerge and set path in .gitconfig
   - Download git
   - Main branch must be names main or master
   - Pull recent changes before running tool.

BookTool Workflow:
   - Open BooktTool.
   - Click on the 'Select Repository' button.
   - Go to a repository of your choice (must contain .git folder).
   - Enter the commitID which was last translated.
   - Click on 'Load Content'.
   - Click on 'Export' to save Changes.txt file in corresponding html folder.
   - Click on file name in File tree on the left to view full file.

Shortcuts:
   Press [Ctrl+G] to go to a specific line in document after content has loaded.
   Press [Ctrl+F] to find occurrences of words.

Output:
   There are two types of outputs:
      1. Diff - opens winmerge with most-recent main on the left and main after entered commitID on the right.
      2. {folder}html/Changes.txt - Saved in corresponding html folder of the repository.

   Contents of the Changes.txt file are displayed in the following format:
   <FilePath to text file where changes were made>
      <LineNumber> <+ | - | -+ > <ChangedLine>
   EOF

   The '+', '-', or '-+' indicate whether a line has been added, removed, or replaced respectively.
      - New lines added: indicated by '+'
      - Lines removed: indicated by '-'
      - All other changes: indicated by '-+'
      - File deleted: indicated by "File Deleted" message.
      - New file added: All new lines are added to Changes.txt preceded by '+'

Changed files are added to file tree on the left.


The changes can also be viewed later by switching to the corresponding branch.
The branches are named as: changesUpto.{mCommitID}. The branch will always be BEHIND main.

