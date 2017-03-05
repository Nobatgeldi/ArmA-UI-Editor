﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Documents;
using ICSharpCode.AvalonEdit.Highlighting;
using ArmA.Studio.UI;
using RealVirtuality.SQF.Parser;
using System.IO;
using ArmA.Studio.DataContext.TextEditorUtil;

namespace ArmA.Studio.DataContext
{
    public class SQFReaderDocument : TextEditorDocument
    {
        private static IHighlightingDefinition ThisSyntaxName { get; set; }
        static SQFReaderDocument()
        {
            ThisSyntaxName = LoadAvalonEditSyntaxFiles(System.IO.Path.Combine(App.SyntaxFilesPath, "sqf.xshd"));
        }
        public override string[] SupportedFileExtensions { get { return new string[] { ".sqf" }; } }
        public override IHighlightingDefinition SyntaxDefinition { get { return ThisSyntaxName; } }

        public BreakPointMargin BreakPointMargin { get; private set; }

        protected override void OnTextEditorSet()
        {
            var sf = Workspace.CurrentWorkspace.CurrentSolution.GetOrCreateFileReference(this.FilePath) as SolutionUtil.SolutionFile;
            this.BreakPointMargin = new BreakPointMargin(sf);
            this.Editor.TextArea.TextView.BackgroundRenderers.Add(this.BreakPointMargin);
            this.Editor.TextArea.LeftMargins.Insert(0, BreakPointMargin);
        }
        protected override IEnumerable<SyntaxError> GetSyntaxErrors()
        {
            using (var memstream = new MemoryStream())
            {
                { //Load content into MemoryStream
                    var writer = new StreamWriter(memstream);
                    writer.Write(this.Document.Text);
                    writer.Flush();
                    memstream.Seek(0, SeekOrigin.Begin);
                }
                //Setup base requirements for the parser
                var parser = new Parser(new Scanner(memstream));
                parser.Parse();
                return parser.errors.ErrorList.Select((it) => new SyntaxError() { StartOffset = it.Item1, Length = it.Item2, Message = it.Item3 });
            }
        }
        protected override IEnumerable<LinterInfo> GetLinterInformations(MemoryStream memstream)
        {
            var inputStream = new Antlr4.Runtime.AntlrInputStream(memstream);

            var lexer = new RealVirtuality.SQF.ANTLR.Parser.sqfLexer(inputStream);
            var commonTokenStream = new Antlr4.Runtime.CommonTokenStream(lexer);
            var p = new RealVirtuality.SQF.ANTLR.Parser.sqfParser(commonTokenStream);
            memstream.Seek(0, SeekOrigin.Begin);

            p.RemoveErrorListeners();
            var se = new List<LinterInfo>();
            p.AddErrorListener(new RealVirtuality.SQF.ANTLR.ErrorListener((recognizer, token, line, charPositionInLine, msg, ex) =>
            {
                se.Add(new LinterInfo()
                {
                    StartOffset = token.StartIndex,
                    Length = token.Text.Length,
                    Message = msg,
                    Severity = ESeverity.Error,
                    Line = line,
                    LineOffset = charPositionInLine,
                    FileName = Path.GetFileName(this.FilePath)
                });
            }));

            var sqfContext = p.sqf();
            return se;

        }
    }
}