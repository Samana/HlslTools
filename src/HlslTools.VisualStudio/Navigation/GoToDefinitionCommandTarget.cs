﻿using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using HlslTools.Compilation;
using HlslTools.VisualStudio.Navigation.GoToDefinitionProviders;
using HlslTools.VisualStudio.Util;
using HlslTools.VisualStudio.Util.Extensions;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.TextManager.Interop;

namespace HlslTools.VisualStudio.Navigation
{
    internal sealed class GoToDefinitionCommandTarget : CommandTargetBase<VSConstants.VSStd97CmdID>
    {
        private readonly IWpfTextView _textView;
        private readonly GoToDefinitionProviderService _goToDefinitionProviderService;
        private readonly SVsServiceProvider _serviceProvider;

        public GoToDefinitionCommandTarget(IVsTextView adapter, IWpfTextView textView, GoToDefinitionProviderService goToDefinitionProviderService, SVsServiceProvider serviceProvider)
            : base(adapter, textView, VSConstants.VSStd97CmdID.GotoDefn)
        {
            _textView = textView;
            _goToDefinitionProviderService = goToDefinitionProviderService;
            _serviceProvider = serviceProvider;
        }

        protected override bool IsEnabled(VSConstants.VSStd97CmdID commandId, ref string commandText)
        {
            // For performance reasons, don't check if we can actually go to definition.
            return true;
        }

        protected override bool Execute(VSConstants.VSStd97CmdID commandId, uint nCmdexecopt, IntPtr pvaIn, IntPtr pvaOut)
        {
            GoToDefinition();
            return true;
        }

        private async void GoToDefinition()
        {
            var pos = _textView.Caret.Position.BufferPosition;

            SemanticModel semanticModel = null;
            if (!await System.Threading.Tasks.Task.Run(() => pos.Snapshot.TryGetSemanticModel(CancellationToken.None, out semanticModel)))
                return;

            var textSpan = _goToDefinitionProviderService.Providers
                .Select(x => x.GetTargetSpan(semanticModel, semanticModel.Compilation.SyntaxTree.MapRootFilePosition(pos.Position)))
                .FirstOrDefault(x => x != null);

            if (textSpan == null)
                return;

            var textDocument = pos.Snapshot.TextBuffer.GetTextDocument();
            GoToLocation(textSpan.Value.Filename ?? textDocument.FilePath, textSpan.Value, null, false);
        }

        // From https://github.com/rsdn/nemerle/blob/master/snippets/VS2010/Nemerle.VisualStudio/LanguageService/NemerleLanguageService.cs#L565
        private void GoToLocation(string filename, HlslTools.Text.TextSpan textSpan, string caption, bool asReadonly)
        {
            uint itemID;
            IVsUIHierarchy hierarchy;
            IVsWindowFrame docFrame;
            IVsTextView textView;

            VsShellUtilities.OpenDocument(_serviceProvider, filename, VSConstants.LOGVIEWID_Code,
              out hierarchy, out itemID, out docFrame, out textView);

            if (asReadonly)
            {
                IVsTextLines buffer;
                ErrorHandler.ThrowOnFailure(textView.GetBuffer(out buffer));
                IVsTextStream stream = (IVsTextStream)buffer;
                stream.SetStateFlags((uint)BUFFERSTATEFLAGS.BSF_USER_READONLY);
            }

            if (caption != null)
                ErrorHandler.ThrowOnFailure(docFrame.SetProperty((int)__VSFPROPID.VSFPROPID_OwnerCaption, caption));

            ErrorHandler.ThrowOnFailure(docFrame.Show());

            if (textView != null)
            {
                var wpfTextView = docFrame.GetWpfTextView();
                var line = wpfTextView.TextBuffer.CurrentSnapshot.GetLineFromPosition(textSpan.Start);
                var span = new TextSpan
                {
                    iStartLine = line.LineNumber,
                    iStartIndex = textSpan.Start - line.Start.Position,
                    iEndLine = line.LineNumber,
                    iEndIndex = textSpan.Start - line.Start.Position
                };

                try
                {
                    ErrorHandler.ThrowOnFailure(textView.SetCaretPos(span.iStartLine, span.iStartIndex));
                    ErrorHandler.ThrowOnFailure(textView.EnsureSpanVisible(span));
                }
                catch (Exception ex)
                {
                    Trace.WriteLine(ex.Message);
                }
            }
        }
    }
}