namespace DemandHighlight
{
    // The third stage of the filter language: the syntax tree bound to meaning, a Pred. Here a word becomes a flag or
    // a type, a key becomes a kind of test, and names in the player's language become the game's identifiers.
    //
    //   Lexer -> Parser -> Ast -> [Binder] -> Pred (-> Lift -> Ast -> Formatter -> text)
    //
    //   key   type | tag | id | condition (cond)
    //   word  owned (mine) | stolen | contraband | true | false | a type's name, through Resolve
    //
    // A scope, key:( ... ), binds its body with that key in force: every bare word and quoted text in it is a value
    // of the key, so type:(Food or "Household Good") is type:Food or type:"Household Good". A field written inside
    // keeps its own key. Scopes add no Pred of their own; they only change how words bind, so everything after this
    // stage (evaluation, ticks, printing) never sees them.
    //
    // Keys and words are not case-sensitive. Type and tag values are the game's identifiers, which are upper case, so
    // unresolved ones are upper-cased; identifiers and conditions are kept as typed. Errors (an unknown key or word,
    // quoted text outside a scope) are thrown as Expr.CompileError with the span of what was written.
    public static class Binder
    {
        // Turns a type or tag as the player wrote it into the game's identifier (a localized type name, for example).
        // For a word on its own that is not a keyword it is asked with key "type", so "food -stolen" works like a
        // search box. Returning null keeps a type:/tag: value upper-cased and makes a word on its own an error.
        public delegate string Resolve(string key, string value);

        public static Pred Bind(Ast node, Resolve resolve)
        {
            return node == null ? Pred.True : Bind(node, resolve, null);
        }

        // scope: the key in force (lower case), or null outside any scope.
        static Pred Bind(Ast node, Resolve resolve, string scope)
        {
            var or = node as OrAst;
            if (or != null)
            {
                var parts = new Pred[or.Parts.Count];
                for (int i = 0; i < parts.Length; i++) parts[i] = Bind(or.Parts[i], resolve, scope);
                return Pred.Or(parts);
            }
            var and = node as AndAst;
            if (and != null)
            {
                var parts = new Pred[and.Parts.Count];
                for (int i = 0; i < parts.Length; i++) parts[i] = Bind(and.Parts[i], resolve, scope);
                return Pred.And(parts);
            }
            var not = node as NotAst;
            if (not != null) return Pred.Not(Bind(not.Operand, resolve, scope));

            var scoped = node as ScopeAst;
            if (scoped != null) return Bind(scoped.Body, resolve, Key(scoped.Key, scoped.KeySpan));

            var field = node as FieldAst;
            if (field != null) return Value(Key(field.Key, field.KeySpan), field.Value, resolve);

            var quoted = node as QuotedAst;
            if (quoted != null)
            {
                if (scope != null) return Value(scope, quoted.Text, resolve);
                throw new Expr.CompileError(Expr.Problem.Unexpected, "\"", quoted.Span);
            }

            var word = (WordAst)node;
            if (scope != null) return Value(scope, word.Text, resolve);
            switch (word.Text.ToLowerInvariant())
            {
                case "owned": case "mine": return Pred.Owned;
                case "stolen": return Pred.Stolen;
                case "contraband": return Pred.Contraband;
                case "true": return Pred.True;
                case "false": return Pred.False;
            }
            var type = resolve != null ? resolve("type", word.Text) : null;
            if (!string.IsNullOrEmpty(type)) return Pred.Type(type);
            throw new Expr.CompileError(Expr.Problem.UnknownWord, word.Text, word.Span);
        }

        // A key as written, checked and in its one spelling.
        static string Key(string written, Span span)
        {
            switch (written.ToLowerInvariant())
            {
                case "type": return "type";
                case "tag": return "tag";
                case "id": return "id";
                case "condition": case "cond": return "condition";
                default: throw new Expr.CompileError(Expr.Problem.UnknownKey, written, span);
            }
        }

        static Pred Value(string key, string value, Resolve resolve)
        {
            switch (key)
            {
                case "type": return Pred.Type(Ident(resolve, "type", value));
                case "tag": return Pred.Tag(Ident(resolve, "tag", value));
                case "id": return Pred.Id(value);
                default: return Pred.Condition(value);
            }
        }

        static string Ident(Resolve resolve, string key, string value)
        {
            var resolved = resolve != null ? resolve(key, value) : null;
            return !string.IsNullOrEmpty(resolved) ? resolved : value.ToUpperInvariant();
        }
    }
}
