using System;
using System.Collections.Generic;

namespace DemandHighlight
{
    // Binding run backwards: a Pred (the intermediate form) lifted to a syntax tree the formatter can write, so text
    // made from a Pred (the window's ticks, the log) and text the player typed go through one formatter.
    //
    //   Lexer -> Parser -> Ast -> Binder -> Pred
    //                       ^                 |
    //                       +----- [Lift] ----+        Ast -> Formatter -> text
    //
    // Lift picks the surface forms: an "or" of values of one key becomes that key over a group, type:(Food or
    // Medical); a set of identifiers becomes id:( ... ); a type is written by its name in the player's language when
    // that name binds back to the same type. Binding the lifted tree gives the Pred back: Bind(Lift(p)) == p.
    public static class Lift
    {
        // typeName gives a type's name as the player writes it; null, or typeName returning null, keeps the
        // identifier. See TypeName.
        public static Ast FromPred(Pred p, Func<string, string> typeName)
        {
            switch (p.Kind)
            {
                case Pred.Op.True: return Word("true");
                case Pred.Op.False: return Word("false");
                case Pred.Op.Owned: return Word("owned");
                case Pred.Op.Stolen: return Word("stolen");
                case Pred.Op.Contraband: return Word("contraband");
                case Pred.Op.Id: case Pred.Op.Tag: case Pred.Op.Type: case Pred.Op.Condition: case Pred.Op.Ask:
                    var value = ValueOf(p, typeName);
                    return new FieldAst { Key = KeyOf(p.Kind), Value = value, Quoted = !Formatter.Bare(value, false) };
                case Pred.Op.IdIn:
                    var ids = new OrAst();
                    foreach (var id in p.Texts) ids.Parts.Add(ScopedValue(id));
                    return new ScopeAst { Key = "id", Body = ids };
                case Pred.Op.Not:
                    return new NotAst { Operand = FromPred(p.Args[0], typeName) };
                case Pred.Op.Or:
                    var key = SharedKey(p);
                    if (key != null)
                    {
                        var values = new OrAst();
                        foreach (var a in p.Args) values.Parts.Add(ScopedValue(ValueOf(a, typeName)));
                        return new ScopeAst { Key = key, Body = values };
                    }
                    var or = new OrAst();
                    foreach (var a in p.Args) or.Parts.Add(FromPred(a, typeName));
                    return or;
                default:
                    var and = new AndAst();
                    foreach (var a in p.Args) and.Parts.Add(FromPred(a, typeName));
                    return and;
            }
        }

        // A type's name as the window writes it: its name in the player's language ("食物", "Household Good"), when
        // that name binds back to the same type through resolve; null otherwise, so the identifier is written. It is
        // always written after type:, so a type never looks like a word ("type:Contraband" is the type, "contraband"
        // the flag). A type name typed on its own is still read (see Binder).
        public static string TypeName(string type, string name, Binder.Resolve resolve)
        {
            if (string.IsNullOrEmpty(name) || resolve == null || resolve("type", name) != type) return null;
            return name;
        }

        static Ast Word(string text) { return new WordAst { Text = text }; }

        // A value inside a group: a word when the lexer reads it back as one and it is not and/or/not, quoted
        // otherwise.
        static Ast ScopedValue(string value)
        {
            return Formatter.Bare(value, true) ? (Ast)new WordAst { Text = value } : new QuotedAst { Text = value };
        }

        // The key every part of an "or" is a value of, or null. Ask is never grouped (players do not type it).
        static string SharedKey(Pred or)
        {
            var kind = or.Args[0].Kind;
            if (kind != Pred.Op.Type && kind != Pred.Op.Tag && kind != Pred.Op.Id && kind != Pred.Op.Condition) return null;
            foreach (var a in or.Args) if (a.Kind != kind) return null;
            return KeyOf(kind);
        }

        static string KeyOf(Pred.Op kind)
        {
            switch (kind)
            {
                case Pred.Op.Type: return "type";
                case Pred.Op.Tag: return "tag";
                case Pred.Op.Id: return "id";
                case Pred.Op.Condition: return "condition";
                default: return "ask";
            }
        }

        static string ValueOf(Pred leaf, Func<string, string> typeName)
        {
            if (leaf.Kind == Pred.Op.Type && typeName != null)
            {
                var name = typeName(leaf.Text);
                if (!string.IsNullOrEmpty(name)) return name;
            }
            return leaf.Text;
        }
    }
}
