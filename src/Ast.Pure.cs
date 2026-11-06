using System.Collections.Generic;

namespace DemandHighlight
{
    // The syntax tree the parser builds: what was written and where, with no meaning yet. "type:食物" is a Field and
    // "stolen" a Word; what they stand for is the binder's business. Brackets leave no node, only their span.
    //
    //   Lexer -> Parser -> [Ast] -> Binder -> Pred (-> Lift -> Ast -> Formatter -> text)
    public abstract class Ast
    {
        public Span Span;

        // The tree as an S-expression, for tests and the log: what the parser understood, with no formatting
        // choices in it. "-a b | c" is (or (and (not a) b) c).
        //   (or ...) (and ...) (not x) (field key value) (scope key body) word "quoted"
        public override string ToString()
        {
            var sb = new System.Text.StringBuilder();
            Dump(sb, this);
            return sb.ToString();
        }

        static void Dump(System.Text.StringBuilder sb, Ast node)
        {
            var or = node as OrAst;
            var and = node as AndAst;
            if (or != null || and != null)
            {
                sb.Append(or != null ? "(or" : "(and");
                foreach (var p in or != null ? or.Parts : and.Parts) { sb.Append(' '); Dump(sb, p); }
                sb.Append(')');
                return;
            }
            var not = node as NotAst;
            if (not != null) { sb.Append("(not "); Dump(sb, not.Operand); sb.Append(')'); return; }
            var scope = node as ScopeAst;
            if (scope != null) { sb.Append("(scope ").Append(scope.Key).Append(' '); Dump(sb, scope.Body); sb.Append(')'); return; }
            var field = node as FieldAst;
            if (field != null)
            {
                sb.Append("(field ").Append(field.Key).Append(' ');
                if (field.Quoted) sb.Append('"').Append(field.Value).Append('"'); else sb.Append(field.Value);
                sb.Append(')');
                return;
            }
            var quoted = node as QuotedAst;
            if (quoted != null) { sb.Append('"').Append(quoted.Text).Append('"'); return; }
            sb.Append(((WordAst)node).Text);
        }
    }

    public sealed class OrAst : Ast { public readonly List<Ast> Parts = new List<Ast>(); }
    public sealed class AndAst : Ast { public readonly List<Ast> Parts = new List<Ast>(); }
    public sealed class NotAst : Ast { public Ast Operand; }

    // key:value. Value is the text without quotes; Quoted says whether it had them.
    public sealed class FieldAst : Ast
    {
        public string Key, Value;
        public Span KeySpan, ValueSpan;
        public bool Quoted;
    }

    // key:( ... ): the key applies to every bare word and quoted text in Body. type:(Food or Medical).
    public sealed class ScopeAst : Ast
    {
        public string Key;
        public Span KeySpan;
        public Ast Body;
    }

    // A word on its own: a keyword such as stolen, or a type's name; inside a scope, a value of its key.
    public sealed class WordAst : Ast { public string Text; }

    // "text" on its own, without its quotes: a value of the scope's key. Outside a scope it means nothing.
    public sealed class QuotedAst : Ast { public string Text; }
}
