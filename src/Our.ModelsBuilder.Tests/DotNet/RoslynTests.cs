﻿using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;
using Our.ModelsBuilder.Building;

namespace Our.ModelsBuilder.Tests.DotNet
{
    public interface IRandom1
    {}

    public interface IRandom2 : IRandom1
    {}

    class TestCodeModel : CodeModel
    {
        public TestCodeModel(CodeModelData data) 
            : base(data)
        { }
    }

    [TestFixture]
    public class RoslynTests
    {
        [Test]
        public void CompilerLanguageVersionTest()
        {
            const string code = @"
class Test
{
    private string GetValue()
    {
        return ""value"";
    }

    // this is csharp v6
    public string Value => this.GetValue();
}
";
            var files = new Dictionary<string, string> { { "source", code } };
            Compiler compiler;

            Assert.Throws<CompilerException>(() =>
            {
                compiler = new Compiler(LanguageVersion.CSharp5);
                compiler.GetCompilation("Our.ModelsBuilder.Generated", files, out _);
            });

            // works
            compiler = new Compiler();
            compiler.GetCompilation("Our.ModelsBuilder.Generated", files, out _);
        }

        [Test]
        public void SemTest1()
        {
            // http://social.msdn.microsoft.com/Forums/vstudio/en-US/64ee86b8-0fd7-457d-8428-a0f238133476/can-roslyn-tell-me-if-a-member-of-a-symbol-is-visible-from-a-position-in-a-document?forum=roslyn
            const string code = @"
using System; // required to properly define the attribute
using Foo;
using Our.ModelsBuilder.Tests;

[assembly:AsmAttribute]

class SimpleClass
{
    public void SimpleMethod()
    {
        Console.WriteLine(""hop"");
    }
}
interface IBase
{}
interface IInterface : IBase
{}
class AnotherClass : SimpleClass, IInterface
{
    class Nested
    {}
}
// if using Foo then reports Foo.Hop
// else just reports Foo which does not exist...
class SoWhat : Hop
{}
[MyAttr]
[SomeAttr(""blue"", Value=555)] // this is a named argument
[SomeAttr(1234)]
[NamedArgsAttribute(s2:""x"", s1:""y"")]
class WithAttr
{}
class Random : IRandom2
{}
class SomeAttrAttribute:Attribute
{
    public SomeAttrAttribute(string s, int x = 55){}
    public int Value { get; set; }
}
class NamedArgsAttribute:Attribute
{
    public NamedArgsAttribute(string s1 = ""a"", string s2 = ""b""){}
}
namespace Foo
{
    // reported as Foo.Hop
    class Hop {}

    class MyAttrAttribute // works
    {}
}";

            // http://msdn.microsoft.com/en-gb/vstudio/hh500769.aspx
            var tree = CSharpSyntaxTree.ParseText(code);
            //var mscorlib = new AssemblyFileReference(typeof(object).Assembly.Location);
            var mscorlib = MetadataReference.CreateFromFile(typeof(object).Assembly.Location);

            // YES! adding the reference and Random1 is found by compilation
            // SO we can get rid of the OmitWhatever attribute!!!!
            // provided that we load everything that's in BIN as references
            // => the CodeInfos must be built on the SERVER and we send files to the SERVER.
            var testslib = MetadataReference.CreateFromFile(typeof(RoslynTests).Assembly.Location);

            var compilation = CSharpCompilation.Create(
                "MyCompilation",
                syntaxTrees: new[] { tree },
                references: new MetadataReference[] { mscorlib, testslib });
            var model = compilation.GetSemanticModel(tree);

            var diags = model.GetDiagnostics();
            if (diags.Length > 0)
            {
                foreach (var diag in diags)
                {
                    Console.WriteLine(diag);
                }
            }

            //var writer = new ConsoleDumpWalker();
            //writer.Visit(tree.GetRoot());

            //var classDeclarations = tree.GetRoot().DescendantNodes(x => x is ClassDeclarationSyntax).OfType<ClassDeclarationSyntax>();
            var classDeclarations = tree.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>();
            foreach (var classDeclaration in classDeclarations)
            {
                Console.WriteLine("class {0}", classDeclaration.Identifier.ValueText);
                var symbol = model.GetDeclaredSymbol(classDeclaration);
                //Console.WriteLine("symbol {0}", symbol.GetType());
                //Console.WriteLine("class {0}", symbol.Name); // just the local name
                var n = SymbolDisplay.ToDisplayString(symbol);
                Console.WriteLine("class {0}", n);
                Console.WriteLine("  : {0}", symbol.BaseType);
                foreach (var i in symbol.Interfaces)
                    Console.WriteLine("  : {0}", i.Name);
                foreach (var i in symbol.AllInterfaces)
                    Console.WriteLine("  + {0} {1}", i.Name, SymbolDisplay.ToDisplayString(i));

                // note: should take care of "error types" => how can we know if there are errors?
                foreach (var asym in symbol.GetAttributes())
                {
                    var t = asym.AttributeClass;
                    Console.WriteLine("  ! {0}", t);
                    if (t is IErrorTypeSymbol)
                    {
                        Console.WriteLine("  ERR");
                    }
                }
            }

            // OK but in our case, compilation of existing code would fail
            // because we haven't generated the missing code already... and yet?

            Console.WriteLine(model);
        }

        [Test]
        public void SemTestMissingReference()
        {
            const string code = @"
using System.Collections.Generic;
using Our.ModelsBuilder.Building;
class MyBuilder : Our.ModelsBuilder.Tests.DotNet.TestCodeModel
{ }
";

            var tree = CSharpSyntaxTree.ParseText(code);
            var refs = new[]
            {
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                //MetadataReference.CreateFromFile(typeof(ReferencedAssemblies).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(RoslynTests).Assembly.Location)
            };

            var options = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                .WithAssemblyIdentityComparer(DesktopAssemblyIdentityComparer.Default)
                .WithStrongNameProvider(new DesktopStrongNameProvider());

            var compilation = CSharpCompilation.Create(
                "Our.ModelsBuilder.RunTests",
                syntaxTrees: new[] { tree },
                references: refs,
                options: options);
            var model = compilation.GetSemanticModel(tree);

            // CS0012: The type '...' is defined in an assembly that is not referenced
            // CS0246: The type or namespace '...' could not be found
            // CS0234: The type or namespace name '...' does not exist in the namespace '...'
            var diags = model.GetDiagnostics();
            if (diags.Length > 0)
            {
                foreach (var diag in diags)
                {
                    Console.WriteLine(diag);
                }
            }

            //Assert.AreEqual(1, diags.Length);
            Assert.GreaterOrEqual(diags.Length, 2);

            Assert.AreEqual("CS0234", diags[0].Id);
            Assert.AreEqual("CS0012", diags[1].Id);
        }

        [Test]
        public void SemTestWithReferences()
        {
            const string code = @"
using System.Collections.Generic;
using Our.ModelsBuilder.Building;
class MyBuilder : Our.ModelsBuilder.Tests.DotNet.TestCodeModel
{ 
    public MyBuilder() : base(null) { }
}
";

            var tree = CSharpSyntaxTree.ParseText(code);
            var refs = new[]
            {
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(ReferencedAssemblies).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(RoslynTests).Assembly.Location)
            };

            var options = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                .WithAssemblyIdentityComparer(DesktopAssemblyIdentityComparer.Default)
                .WithStrongNameProvider(new DesktopStrongNameProvider());

            var compilation = CSharpCompilation.Create(
                "Our.ModelsBuilder.RunTests",
                syntaxTrees: new[] { tree },
                references: refs,
                options: options);
            var model = compilation.GetSemanticModel(tree);

            var diags = model.GetDiagnostics().Where(x => x.Severity != DiagnosticSeverity.Hidden).ToArray();
            if (diags.Length > 0)
            {
                foreach (var diag in diags)
                {
                    Console.WriteLine(diag);
                }
            }

            Assert.AreEqual(0, diags.Length);
        }

        [Test]
        public void SemTestAssemblyAttributes()
        {
            const string code = @"
using System;
[assembly: Nevgyt(""yop"")]
[assembly: Shmuit]

class Shmuit:Attribute
{}

[Fooxy(""yop"")]
class SimpleClass
{
    [Funky(""yop"")]
    public void SimpleMethod()
    {
        var list = new List<string>();
        list.Add(""first"");
        list.Add(""second"");
        var result = from item in list where item == ""first"" select item;
    }
}";

            var tree = CSharpSyntaxTree.ParseText(code);
            var mscorlib = MetadataReference.CreateFromFile(typeof(object).Assembly.Location);

            var compilation = CSharpCompilation.Create(
                "MyCompilation",
                syntaxTrees: new[] { tree },
                references: new MetadataReference[] { mscorlib });
            _ = compilation.GetSemanticModel(tree);
            foreach (var attrData in compilation.Assembly.GetAttributes())
            {
                var attrClassSymbol = attrData.AttributeClass;

                // handle errors
                if (attrClassSymbol is IErrorTypeSymbol) continue;
                if (attrData.AttributeConstructor == null) continue;

                var attrClassName = SymbolDisplay.ToDisplayString(attrClassSymbol);
                Console.WriteLine(attrClassName);
            }
        }

        [Test]
        public void ParseTest1()
        {
            const string code = @"
[assembly: Nevgyt(""yop"")]
[Fooxy(""yop"")]
class SimpleClass
{
    [Funky(""yop"")]
    public void SimpleMethod()
    {
        var list = new List<string>();
        list.Add(""first"");
        list.Add(""second"");
        var result = from item in list where item == ""first"" select item;
    }
}";

            var tree = CSharpSyntaxTree.ParseText(code);
            var writer = new ConsoleDumpWalker();
            writer.Visit(tree.GetRoot());
        }

        [Test]
        public void ParseTest2()
        {
            const string code = @"
using Our.ModelsBuilder;

[assembly: Generator.IgnoreContentType(""ccc"")]

namespace Umbrco.Web.Models.User
{
    // don't create a model for ddd
    // IGNORED should be out of the namespace
    [assembly: Generator.IgnoreContentType(""ddd"")]

    // create a mixin for MixinTest but with a different class name
    [PublishedModel(""MixinTest"")]
    public partial interface IMixinTestRenamed
    { }

    // create a model for bbb but with a different class name
    [PublishedModel(""bbb"")]
    public partial class SpecialBbb
    { }

    // create a model for ...
    [Generator.IgnorePropertyType(""nomDeLEleve"")] // but don't include that property
    public partial class LoskDalmosk
    {
    }

    // create a model for page...
    public partial class Page
    {
        // but don't include that property because I'm doing it
        [Generator.IgnorePropertyType(""alternativeText"")]
        public AlternateText AlternativeText => this.Value<AlternateText>(""alternativeText"");
    }
}
";

            var tree = CSharpSyntaxTree.ParseText(code);
            var writer = new TestWalker();
            writer.Visit(tree.GetRoot());
        }

        [Test]
        public void ParseTest3()
        {
            const string code = @"
class SimpleClass1 : BaseClass, ISomething, ISomethingElse
{
}
class SimpleClass2
{
}";

            var tree = CSharpSyntaxTree.ParseText(code);
            var writer = new ConsoleDumpWalker();
            writer.Visit(tree.GetRoot());
        }

        [Test]
        public void ParseTest4()
        {
            const string code = @"
[SomeAttribute(""value1"", ""value2"")]
[SomeOtherAttribute(Foo:""value1"", BaDang:""value2"")]
class SimpleClass1
{
}";

            var tree = CSharpSyntaxTree.ParseText(code);
            var writer = new ConsoleDumpWalker();
            writer.Visit(tree.GetRoot());
        }

        [Test]
        public void ParseTest5()
        {
            const string code = @"


[SomeAttribute(SimpleClass1.Const)]
[SomethingElse(Foo.Blue|Foo.Red|Foo.Pink)]
[SomethingElse(Foo.Blue)]
class SimpleClass1
{
    public const string Const = ""const"";
}";

            var tree = CSharpSyntaxTree.ParseText(code);
            var writer = new ConsoleDumpWalker();
            writer.Visit(tree.GetRoot());
        }

        [Test]
        public void ParseAndDetectErrors()
        {
            const string code = @"

class MyClass
{
poo
}";
            var tree = CSharpSyntaxTree.ParseText(code);
            var diags = tree.GetDiagnostics().ToArray();
            Assert.AreEqual(1, diags.Length);
            var diag = diags[0];
            Assert.AreEqual("CS1519", diag.Id);
        }

        [Test]
        public void ParseAndDetectNoError()
        {
            const string code = @"

[Whatever]
class MyClass
{
}";
            var tree = CSharpSyntaxTree.ParseText(code);
            var diags = tree.GetDiagnostics().ToArray();
            Assert.AreEqual(0, diags.Length);
            // unknown attribute is a semantic error
        }

        [Test]
        public void SymbolLookup_Ambiguous1()
        {
            const string code = @"
using System.Text; // imports ASCIIEncoding
using Our.ModelsBuilder.Tests; // imports ASCIIEncoding
using Our.ModelsBuilder.Tests.DotNet; // imports RoslynTests
namespace SomeCryptoNameThatDoesReallyNotExistEgAGuid
{ }
";

            GetSemantic(code, out var model, out var pos);

            Assert.AreEqual(1, LookupSymbol(model, pos, null, "StringBuilder"));
            Assert.AreEqual(1, LookupSymbol(model, pos, null, "RoslynTests"));

            // imported twice
            Assert.AreEqual(2, LookupSymbol(model, pos, null, "ASCIIEncoding"));
        }

        [Test]
        public void SymbolLookup_Ambiguous2()
        {
            const string code = @"
using System.Text; // imports ASCIIEncoding
using Our.ModelsBuilder.Tests.DotNet; // imports ASCIIEncoding
namespace Our.ModelsBuilder.Tests.Models // forces Our.ModelsBuilder.Tests.ASCIIEncoding
{ }
";

            GetSemantic(code, out var model, out var pos);

            Assert.AreEqual(1, LookupSymbol(model, pos, null, "StringBuilder"));
            Assert.AreEqual(1, LookupSymbol(model, pos, null, "RoslynTests"));

            // imported twice, forced, NOT ambiguous !
            Assert.AreEqual(1, LookupSymbol(model, pos, null, "ASCIIEncoding"));
        }

        [Test]
        public void SymbolLookup_AmbiguousNamespace1()
        {
            const string code = @"
namespace SomeCryptoNameThatDoesReallyNotExistEgAGuid
{ }
";

            GetSemantic(code, out var model, out var pos);

            Assert.AreEqual(0, LookupSymbol(model, pos, null, "String"));
        }

        [Test]
        public void SymbolLookup_AmbiguousNamespace2()
        {
            const string code = @"
namespace System.Models
{ }
";

            GetSemantic(code, out var model, out var pos);

            // implicit using System
            Assert.AreEqual(1, LookupSymbol(model, pos, null, "String"));
        }

        [Test]
        public void SymbolLookup_AmbiguousNamespace3()
        {
            const string code = @"
namespace System.Models
{ }
";

            GetSemantic(code, out var model, out var pos);

            // implicit using System
            Assert.AreEqual(1, LookupSymbol(model, pos, null, "Collections"));
        }

        [Test]
        public void SymbolLookup_AmbiguousNamespace4()
        {
            const string code = @"
namespace Our.ModelsBuilder
{
    public class Test
    {
        // this needs global:: to compile
        public global::Umbraco.Core.Models.PublishedContent.IPublishedContent Content;
    }
}
";

            GetSemantic(code, out var model, out var pos);

            // implicit
            // finds Our.ModelsBuilder.Umbraco
            // but we thought we'd find global Umbraco.Core.Models.IPublishedContent
            // so it is NOT ambiguous but will not compile because Our.ModelsBuilder.Umbraco.Core... does not exist
            var lookup = model.LookupNamespacesAndTypes(pos, null, "Umbraco");
            Assert.AreEqual(1, lookup.Length);
            Assert.AreEqual("Our.ModelsBuilder.Umbraco", lookup[0].ToDisplayString());

            // fullName => "Umbraco" has to be the top-level namespace
            //  so what the lookup returns must be exactly "Umbraco" else ambiguous
            // non-fullName => must be a complete path to type
            var match = "Umbraco";
            Assert.AreNotEqual(match, lookup[0].ToDisplayString());

            var files = new Dictionary<string, string> { { "source", code } };

            var compiler = new Compiler();
            var compilation = compiler.GetCompilation("Our.ModelsBuilder.Generated", files, out _);
            foreach (var diag in compilation.GetDiagnostics())
                Console.WriteLine(diag);
        }

        [Test]
        public void SymbolLookup()
        {
            const string code = @"
using System.Collections.Generic;
using System.Text;
namespace MyNamespace
{
    // %%POS%%

    public class MyClass
    {
        public MyClass()
        { }

        public void Do()
        { }
    }

    public class OtherClass
    {
        public class NestedClass { }
        private class HiddenClass { }
    }
}
";
            GetSemantic(code, out var model, out var pos);

            // only in proper scope
            Assert.AreEqual(1, LookupSymbol(model, pos, null, "MyClass"));
            Assert.AreEqual(0, LookupSymbol(model, 0, null, "MyClass"));

            // not! looking for symbols only
            Assert.AreEqual(0, LookupSymbol(model, 0, null, "MyNamespace.MyClass"));
            Assert.AreEqual(0, LookupSymbol(model, pos, null, "MyNamespace.MyClass"));

            // yes!
            Assert.AreEqual(1, LookupSymbol(model, 0, null, "StringBuilder"));
            Assert.AreEqual(1, LookupSymbol(model, pos, null, "StringBuilder"));

            // not! looking for symbols only
            Assert.AreEqual(0, LookupSymbol(model, 0, null, "System.Text.StringBuilder"));
            Assert.AreEqual(0, LookupSymbol(model, 0, null, "global::StringBuilder"));
            Assert.AreEqual(0, LookupSymbol(model, 0, null, "global::System.Text.StringBuilder"));

            // cannot find Int32 at root because of no using clause
            Assert.AreEqual(0, LookupSymbol(model, 0, null, "Int32"));

            // can find System.Collections.Generic.Dictionary<TKey, TValue> (using clause)
            Assert.AreEqual(1, LookupSymbol(model, 0, null, "Dictionary"));

            // can find OtherClass within MyNamespace, & nested...
            Assert.AreEqual(1, LookupSymbol(model, pos, null, "OtherClass"));

            // that's not how it works
            //Assert.AreEqual(0, LookupSymbol(model, pos, null, "NestedClass"));
            //Assert.AreEqual(1, LookupSymbol(model, pos, null, "OtherClass.NestedClass"));
            //Assert.AreEqual(0, LookupSymbol(model, pos, null, "OtherClass+HiddenClass"));
        }

        private void GetSemantic(string code, out SemanticModel model, out int pos)
        {
            var tree = CSharpSyntaxTree.ParseText(code);

            //var writer = new ConsoleDumpWalker();
            //writer.Visit(tree.GetRoot());

            var refs = ReferencedAssemblies.Locations
                .Distinct()
                .Select(x => MetadataReference.CreateFromFile(x));

            var compilation = CSharpCompilation.Create(
                "MyCompilation",
                syntaxTrees: new[] { tree },
                references: refs);
            model = compilation.GetSemanticModel(tree);

            var diags = model.GetDiagnostics();
            foreach (var diag in diags)
            {
                if (diag.Id == "CS8019") continue; // Unnecessary using directive.
                Console.WriteLine(diag);
                Assert.Fail();
            }

            var ns = tree.GetRoot().DescendantNodes().OfType<NamespaceDeclarationSyntax>().First();
            pos = ns.OpenBraceToken.SpanStart;

            // if we use that as a "container" then we get what's in the container *only*
            // not what we want here, then we have to use the position to determine *scope*
            //var ss = model.GetDeclaredSymbol(ns);
        }

        private int LookupSymbol(SemanticModel model, int pos, INamespaceOrTypeSymbol container, string symbol)
        {
            Console.WriteLine("lookup: " + symbol);
            //var symbols = model.LookupSymbols(0, container, symbol);
            var symbols = model.LookupNamespacesAndTypes(pos, container, symbol);
            foreach (var x in symbols)
                Console.WriteLine(x.ToDisplayString());
            return symbols.Length;
        }
    }

    internal class TestWalker : CSharpSyntaxWalker
    {
        //private string _propertyName;
        private string _attributeName;
        private readonly Stack<string> _classNames = new Stack<string>();

        public override void VisitLiteralExpression(LiteralExpressionSyntax node)
        {
            if (_attributeName != null)
            {
                string className;
                //Console.WriteLine("ATTRIBUTE VALUE {0}", node.Token.ValueText);
                switch (_attributeName)
                {
                    case "Generator.IgnoreContentType":
                        Console.WriteLine("Ignore ContentType {0}", node.Token.ValueText);
                        break;
                    case "Generator.IgnorePropertyType":
                        className = _classNames.Peek();
                        Console.WriteLine("Ignore PropertyType {0}.{1}", className, node.Token.ValueText);
                        break;
                    case "PublishedModel":
                        className = _classNames.Peek();
                        Console.WriteLine("Name {0} for ContentType {1}", className, node.Token.ValueText);
                        break;
                }
            }
            base.VisitLiteralExpression(node);
        }

        public override void VisitAttribute(AttributeSyntax node)
        {
            //Console.WriteLine("ATTRIBUTE {0}", node.Name);
            _attributeName = node.Name.ToString();
            base.VisitAttribute(node);
            _attributeName = null;
        }

        public override void VisitInterfaceDeclaration(InterfaceDeclarationSyntax node)
        {
            //Console.WriteLine("BEGIN INTERFACE {0}", node.Identifier);
            _classNames.Push(node.Identifier.ToString());
            base.VisitInterfaceDeclaration(node);
            _classNames.Pop();
            //Console.WriteLine("END INTERFACE {0}", node.Identifier);
        }

        public override void VisitClassDeclaration(ClassDeclarationSyntax node)
        {
            //Console.WriteLine("BEGIN CLASS {0}", node.Identifier);
            _classNames.Push(node.Identifier.ToString());
            base.VisitClassDeclaration(node);
            _classNames.Pop();
            //Console.WriteLine("END CLASS {0}", node.Identifier);
        }

        //public override void VisitPropertyDeclaration(PropertyDeclarationSyntax node)
        //{
        //    _propertyName = node.Identifier.ToString();
        //    base.VisitPropertyDeclaration(node);
        //    _propertyName = null;
        //}

        public override void Visit(SyntaxNode node)
        {
            var padding = node.Ancestors().Count();
            var prepend = node.ChildNodes().Any() ? "[-]" : "[.]";
            var line = new string(' ', padding) + prepend + " " + node.GetType();
            Console.WriteLine(line);
            base.Visit(node);
        }
    }

    internal class ConsoleDumpWalker : CSharpSyntaxWalker
    {
        private const string Prefix = "Microsoft.CodeAnalysis.";

        public override void VisitToken(SyntaxToken token)
        {
            Console.WriteLine("TK:" + token);
            base.VisitToken(token);
        }

        public override void Visit(SyntaxNode node)
        {
            var padding = node.Ancestors().Count();
            var prepend = node.ChildNodes().Any() ? "[-]" : "[.]";
            var nodetype = node.GetType().FullName;
            if (nodetype == null)
                throw new Exception();
            if (nodetype.StartsWith(Prefix)) nodetype = nodetype.Substring(Prefix.Length);
            var line = new string(' ', padding) + prepend + " " + nodetype;
            Console.WriteLine(line);

            //var decl = node as ClassDeclarationSyntax;
            //if (decl != null && decl.BaseList != null)
            //{
            //    Console.Write(new string(' ', padding + 4) + decl.Identifier);
            //    foreach (var n in decl.BaseList.Types.OfType<IdentifierNameSyntax>())
            //    {
            //        Console.Write(" " + n.Identifier);
            //    }
            //    Console.WriteLine();
            //}

            if (node is AttributeSyntax attr)
            {
                Console.WriteLine(new string(' ', padding + 4) + "> " + attr.Name);
                foreach (var arg in attr.ArgumentList.Arguments)
                {
                    var expr = arg.Expression as LiteralExpressionSyntax;
                    //Console.WriteLine(new string(' ', padding + 4) + "> " + arg.NameColon + " " + arg.NameEquals);
                    Console.WriteLine(new string(' ', padding + 4) + "> " + expr?.Token.Value);
                }
            }

            if (node is IdentifierNameSyntax attr2)
            {
                Console.WriteLine(new string(' ', padding + 4) + "T " + attr2.Identifier.GetType());
                Console.WriteLine(new string(' ', padding + 4) + "V " + attr2.Identifier);
            }

            if (node is TypeSyntax x)
            {
                var xtype = x.GetType().FullName;
                if (xtype == null)
                    throw new Exception();
                if (xtype.StartsWith(Prefix)) xtype = nodetype.Substring(Prefix.Length);
                Console.WriteLine(new string(' ', padding + 4) + "> " + xtype);
            }

            base.Visit(node);
        }
    }
}
