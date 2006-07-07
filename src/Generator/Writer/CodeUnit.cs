using System;
using System.Collections;
using System.Collections.Specialized;
using System.IO;

using Mono.CSharp;
using System.CodeDom;

using System.CodeDom.Compiler;
using Microsoft.CSharp;
using System.Text;

namespace Spring2.DataTierGenerator.Generator.Writer {

    /// <summary>
    /// Wrapper for creating and merging CodeCompileUnits
    /// </summary>
    public class CodeUnit {

	private ArrayList src;
	private CodeCompileUnit unit;
	private IList log;
	private CodeGeneratorOptions cgOptions = new CodeGeneratorOptions ();
    	private Boolean hadError = false;

	public CodeUnit(String filename, Stream stream, IList log, CodeGeneratorOptions options) {
	    System.IO.TextWriter cout = Console.Out;
	    System.IO.TextWriter cerr = Console.Error;

	    try {
		this.log = log;
		this.cgOptions = options;

		StringWriter sw = new StringWriter();
		Console.SetOut(sw);
		Console.SetError(sw);
		CSharpParser p = new CSharpParser(filename, stream, null);

		int errno = p.parse();

		if (errno>0 || Report.Warnings>0) {
		    this.log.Add(sw.ToString());
		}

		unit = p.Builder.CurrCompileUnit;
		stream.Position=0;
		src = GetSourceLines(new StreamReader(stream).ReadToEnd());

		ExtractMemberBodies();
	    } catch (Exception ex) {
	    	// TODO: shouldn't something happen here so that it is known that the code unit is not valid?
		Console.Out.WriteLine("Unhandled exception parsing file: " + filename, ex);
	    	hadError = true;
	    } finally {
		Console.SetOut(cout);
		Console.SetError(cerr);
	    }
	}

	public CodeCompileUnit Unit {
	    get { return this.unit; }
	}

    	public bool HadError {
    		get { return hadError; }
    	}

    	private ArrayList GetSourceLines(String source) {
	    ArrayList lines = new ArrayList();
	    StringReader reader = new StringReader(source);
	    
	    String line = reader.ReadLine();
	    while (line != null) {
		lines.Add(line);
		line = reader.ReadLine();
	    }
	    return lines;
	}

	private String GetSource(Member member, Int32 nestingLevel) {
	    if ((member.Element.Attributes & MemberAttributes.ScopeMask) == MemberAttributes.Abstract) {
		return String.Empty;
	    }

	    StringBuilder buffer = new StringBuilder();
	    for (Int32 i = member.FirstLine; i <= member.LastLine && i < src.Count; i++) {
		buffer.Append(src[i-1].ToString()).Append(Environment.NewLine);
	    }

	    String hold = buffer.ToString();
	    String s = buffer.ToString();

	    s = s.Substring(s.IndexOf("{")+1);

	    ArrayList source = GetSourceLines(s);

	    // Remove the comment and attribute lines that belong to the next method.
	    while (source[source.Count-1].ToString().Trim().StartsWith("#endregion") || source[source.Count-1].ToString().Trim().StartsWith("#region") || source[source.Count-1].ToString().Trim().StartsWith("//") || source[source.Count-1].ToString().Trim().StartsWith("[") || source[source.Count-1].ToString().Trim().Length == 0) {
		if (source[source.Count-1].ToString().Trim().StartsWith("#region")) {
		    member.HasBeginRegion = true;
		    member.RegionName = source[source.Count-1].ToString().Trim().Substring(7).Trim();
		}
		source.RemoveAt(source.Count-1);
	    }

	    if (source[source.Count-1].ToString().Trim().Equals("}")) {
		source.RemoveAt(source.Count-1);
	    } else {
		String line = source[source.Count-1].ToString();
		if (line.LastIndexOf("}")>=0) {
		    source[source.Count-1] = line.Substring(0, line.LastIndexOf("}")-1);
		} else {
		    throw new Exception("could not find closing brace: " + hold);
		}
	    }

	    // If this is the last member, remove the extra curly brace.
	    if (member.LastLine >= 9999) {
		RemoveEmptyLinesFromEnd(source);

		if (source[source.Count-1].ToString().Trim().Equals("}")) {
		    Int32 removed = 0;
		    while(source[source.Count-1].ToString().Trim().Equals("}") && removed <= nestingLevel) {
			source.RemoveAt(source.Count-1);
			removed++;
		    }
		} else {
		    // remove #endregion lines
		    while (source[source.Count-1].ToString().Trim().StartsWith("#endregion")) {
			member.HasEndRegion = true;
			source.RemoveAt(source.Count-1);
		    }

		    RemoveEmptyLinesFromEnd(source);

		    String line = source[source.Count-1].ToString();
		    if (line.LastIndexOf("}")>=0) {
			source[source.Count-1] = line.Substring(0,line.LastIndexOf("}")-1);
		    } else {
			throw new Exception(">>>" + s + "<<<");
		    }
		}
	    }

	    RemoveEmptyLinesFromBeginning(source);

	    buffer = new StringBuilder();
	    foreach(String line in source) {
		buffer.Append(line).Append(Environment.NewLine);
	    }

	    s = buffer.ToString().Trim();
	    return s;
	}

    	/// <summary>
    	/// Remove emtpy lines at the beginning of the method block.
    	/// </summary>
    	/// <param name="source"></param>
	private static void RemoveEmptyLinesFromBeginning(IList source) {
	    while (source.Count > 0 && source[0].ToString().Trim().Length == 0) {
		source.RemoveAt(0);
	    }
	}

    	/// <summary>
    	/// Remove emtpy lines at the end of the method block.
    	/// </summary>
    	/// <param name="source"></param>
	private static void RemoveEmptyLinesFromEnd(IList source) {
	    while (source[source.Count-1].ToString().Trim().Length == 0) {
		source.RemoveAt(source.Count-1);
	    }
	}

    	private String GetGetSource(Member member) {
	    String body = GetSource(member, 0);

	    // no get statement
	    if (!body.StartsWith("get")) {
		return String.Empty;
	    } else {
		body = body.Substring(3).Trim();
		body = body.Substring(1).Trim();

		Int32 count = 0;
		StringBuilder sb = new StringBuilder();
		while (count >=0 && body.Length>0) {
		    if (body.Substring(0,1).Equals("{")) {
			count++;
		    }
		    if (body.Substring(0,1).Equals("}")) {
			count--;
		    }
		    sb.Append(body.Substring(0,1));
		    body = body.Substring(1);
		}
		body = sb.ToString().Trim();
		return body.Substring(0,body.Length-1).Trim();
	    }
	}

	private String GetSetSource(Member member) {
	    String body = GetSource(member, 0).Trim();

	    // no get statement
	    if (!body.StartsWith("get")) {
		if (body.StartsWith("set")) {
		    body = body.Substring(3).Trim();
		    body = body.Substring(1, body.Length-2).Trim();

		    return body;
		}
	    } else {
		body = body.Substring(3).Trim();
		body = body.Substring(1).Trim();

		Int32 count = 0;
		while (count >=0 && body.Length>0) {
		    if (body.Substring(0,1).Equals("{")) {
			count++;
		    }
		    if (body.Substring(0,1).Equals("}")) {
			count--;
		    }
		    body = body.Substring(1).Trim();
		}

		if (body.StartsWith("set")) {
		    body = body.Substring(3).Trim();
		    body = body.Substring(1, body.Length-2).Trim();

		    return body;
		}

	    }

	    return String.Empty;
	}

	private CodeCommentStatementCollection ParseFieldComments(Member member) {
	    StringCollection source = new StringCollection();
	    Int32 i = member.FirstLine - 2;
	    while (i >= 0 && (src[i].ToString().Trim().StartsWith("//") || src[i].ToString().Trim().StartsWith("[") || src[i].ToString().Trim().Length==0)) {
		if (src[i].ToString().Trim().StartsWith("//") || src[i].ToString().Trim().Length==0) {
		    source.Insert(0, src[i].ToString());
		}
		i--;
	    }

	    RemoveEmptyLinesFromBeginning(source);
	    
	    CodeCommentStatementCollection comments = new CodeCommentStatementCollection();
	    foreach(String line in source) {
		String s = line.Trim();
		if (s.StartsWith("///")) {
	    	    comments.Add(new CodeCommentStatement(s.Substring(3).Trim(), true));
		} else if (s.StartsWith("//")) {
		    comments.Add(new CodeCommentStatement(s.Substring(2).Trim()));
		} else {
		    comments.Add(new CodeCommentStatement(s));
		}
	    }

	    return comments;
	}

    	private void ExtractMemberBodies() {
	    ArrayList members1 = GetMembers(unit);
	    ExtractMemberBodies(members1, 0);
	}

	private void ExtractMemberBodies(ArrayList members1, Int32 nestingLevel) {
	    //Int32 lastLine = 0;
	    foreach(Member member in members1) {
		//log.Add("found " + (member.Generate ? "[generate] " : "") + member.Element.GetType().FullName + ": " + member.Element.Name + " starting at line " + member.FirstLine.ToString() + " and ending on line " + member.LastLine.ToString());
		if (member.Element is CodeMemberField) {
		    member.Element.Comments.Clear();
		    member.Element.Comments.AddRange(ParseFieldComments(member));
		} else if (member.Element is CodeMemberMethod) {
		    String s = GetSource(member, nestingLevel);
		    if (member.HasBeginRegion) {
			//log.Add("found #region " + member.RegionName);
			//Int32 index = member.Type.Members.IndexOf(member.Element);
			//member.Type.Members.Insert(index, new CodeSnippetTypeMember("#region " + member.RegionName));
		    }
		    ((CodeMemberMethod)member.Element).Statements.Clear();
		    ((CodeMemberMethod)member.Element).Statements.Add(new CodeSnippetStatement(s));
		    if (member.HasEndRegion) {
			//log.Add("found #endregion");
			//member.Type.Members.Insert(index, new CodeSnippetTypeMember("#endregion"));
		    }
		} else if (member.Element is CodeMemberProperty) {
		    ((CodeMemberProperty)member.Element).GetStatements.Clear();
		    ((CodeMemberProperty)member.Element).SetStatements.Clear();
		    String get = GetGetSource(member);
		    if (get.Length > 0) {
			((CodeMemberProperty)member.Element).GetStatements.Add(new CodeSnippetStatement(get));
		    }
		    String set = GetSetSource(member);
		    if (set.Length > 0) {
			((CodeMemberProperty)member.Element).SetStatements.Add(new CodeSnippetStatement(set));
		    }
		} else if (member.Element is CodeTypeDeclaration) {
		    ExtractMemberBodies(GetMembers((CodeTypeDeclaration)member.Element, member.LastLine), ++nestingLevel);
		} else {
		    throw new Exception("Can't extract body from unknown member type: " + member.Element.GetType().FullName);
		}
	    }
	}

	public void Merge(CodeUnit mergeUnit) {
	    // add new namespaces
	    // add imports
	    // add types

	    // add members

	    foreach(CodeNamespace mergeNamespace in mergeUnit.Unit.Namespaces) {
		if (!HasNamespace(unit, mergeNamespace.Name)) {
		    unit.Namespaces.Add(mergeNamespace);
		} else {
		    CodeNamespace ns = GetNamespace(unit, mergeNamespace.Name);
		    foreach(CodeNamespaceImport import in mergeNamespace.Imports) {
			if (!HasImport(ns, import.Namespace)) {
			    ns.Imports.Add(import);
			}
		    }
		    foreach(CodeTypeDeclaration mergeType in mergeNamespace.Types) {
			if (!HasType(ns, mergeType.Name)) {
			    ns.Types.Add(mergeType);
			} else {
			    CodeTypeDeclaration type = GetType(ns, mergeType.Name);
			    Merge(type, mergeType);
			}
		    }

		}
	    }

	    // search for members with the Generate attribute that are no longer in the in the generated code
	    // Note that we probably need to do something more here to remove members of nested types. (I.E. internal classes like ColumnOrdinals)
	    foreach(CodeNamespace mergeNamespace in unit.Namespaces) {
		if (HasNamespace(mergeUnit.Unit, mergeNamespace.Name)) {
		    CodeNamespace ns = GetNamespace(mergeUnit.Unit, mergeNamespace.Name);
		    foreach(CodeTypeDeclaration mergeType in mergeNamespace.Types) {
			if (HasType(ns, mergeType.Name)) {
			    CodeTypeDeclaration type = GetType(ns, mergeType.Name);
			    ArrayList remove = new ArrayList();
			    foreach(CodeTypeMember mergeMember in mergeType.Members) {
				if (!HasMember(type, mergeMember)) {
				    if (IsMemberGenerated(mergeMember)) {
					//log.Add("removing member with [Generate] attribute that is no longer in generated code: " + mergeType.Name + "." + mergeMember.Name);
					remove.Add(mergeMember);
				    }
				}
			    }
			    foreach(CodeTypeMember o in remove) {
				mergeType.Members.Remove(o);
			    }
			}
		    }

		}
	    }

	    
	}

	private void Merge(CodeTypeDeclaration type, CodeTypeDeclaration mergeType) {
	    // TODO: attributes
	    // TODO: basetypes
	    // TODO: comments
	    // TODO: custom attributes
	    // TODO: type attributes
	    foreach(CodeTypeMember mergeMember in mergeType.Members) {
		if (!HasMember(type, mergeMember)) {
		    //log.Add("adding member: " + mergeMember.Name);
		    type.Members.Add(mergeMember);
		} else {
		    if (mergeMember is CodeTypeDeclaration) {
			// For now don't merge nested classes.  We need to have a way of getting the type from the class instead of the namespace.
			CodeTypeDeclaration nestedType = (CodeTypeDeclaration)GetMember(type, mergeMember);
			Merge(nestedType, (CodeTypeDeclaration)mergeMember);
		    } else {
			UpdateMember(type, mergeMember);
		    }
		}
	    }
	}

	private CodeNamespace GetNamespace(CodeCompileUnit unit, String name) {
	    foreach(CodeNamespace ns in unit.Namespaces) {
		if (ns.Name.Equals(name)) {
		    return ns;
		}
	    }
	    return null;
	}

	private CodeNamespaceImport GetImport(CodeNamespace ns, String name) {
	    foreach(CodeNamespaceImport import in ns.Imports) {
		if (import.Namespace.Equals(name)) {
		    return import;
		}
	    }
	    return null;
	}

	private CodeTypeDeclaration GetType(CodeNamespace ns, String name) {
	    foreach(CodeTypeDeclaration type in ns.Types) {
		if (type.Name.Equals(name)) {
		    return type;
		}
	    }
	    return null;
	}

	private CodeTypeMember GetMember(CodeTypeDeclaration type, CodeTypeMember mergeMember) {
	    foreach(CodeTypeMember member in type.Members) {
		if (member.Name.Equals(mergeMember.Name)) {
		    Boolean match = true;
		    if (member is CodeMemberMethod && mergeMember is CodeMemberMethod) {
			CodeMemberMethod method = (CodeMemberMethod)member;
			CodeMemberMethod mergeMethod = (CodeMemberMethod)mergeMember;
			if (method.Parameters.Count == mergeMethod.Parameters.Count) {
			    foreach(CodeParameterDeclarationExpression parameter in mergeMethod.Parameters) {
				if (!HasParameter(method, parameter.Type)) {
				    match = false;
				}
			    }
			} else {
			    match = false;
			}
		    } else if (member is CodeMemberProperty) {
			match = true;
		    } else if (member is CodeMemberField) {
			match = true;
		    } else if (member is CodeTypeDeclaration) {
			match = true;
		    } else {
			throw new Exception("unable to get member of type: " + mergeMember.GetType().FullName);
		    }
		    if (match) {
			return member;
		    }
		}
	    }
	    return null;
	}

	private CodeParameterDeclarationExpression GetParameter(CodeMemberMethod method, CodeTypeReference type) {
	    foreach(CodeParameterDeclarationExpression parameter in method.Parameters) {
		if (parameter.Type.BaseType.Equals(type.BaseType)) {
		    return parameter;
		}
	    }
	    return null;
	}

	private Boolean HasNamespace(CodeCompileUnit unit, String name) {
	    return GetNamespace(unit, name) == null ? false : true;
	}

	private Boolean HasImport(CodeNamespace ns, String name) {
	    return GetImport(ns, name) == null ? false : true;
	}

	private Boolean HasType(CodeNamespace ns, String name) {
	    return GetType(ns, name) == null ? false : true;
	}

	private Boolean HasMember(CodeTypeDeclaration type, CodeTypeMember member) {
	    return GetMember(type, member) == null ? false : true;
	}

	private Boolean HasParameter(CodeMemberMethod method, CodeTypeReference type) {
	    return GetParameter(method, type) == null ? false : true;
	}

	private void UpdateMember(CodeTypeDeclaration type, CodeTypeMember mergeMember) {
	    CodeTypeMember member = GetMember(type, mergeMember);

	    if (IsMemberGenerated(member)) {
		Int32 index = type.Members.IndexOf(member);
		type.Members[index] = mergeMember;
	    } else {
		if (IsMemberGenerated(mergeMember)) {
		    //log.Add("member " + type.Name + "." + member.Name + " was found in generated source but was not overwritten because it does not have the Generate attribute.");
		}
	    }
	}

	private Boolean IsMemberGenerated(CodeTypeMember member) {
	    foreach(CodeAttributeDeclaration cad in member.CustomAttributes) {
		if (cad.Name.Equals("Generate")) {
		    return true;
		}
	    }
	    return false;
	}

	public String Generate() {
	    CodeDomProvider provider= new CSharpCodeProvider();
	    StringWriter sw = new StringWriter();
	    ICodeGenerator generator= provider.CreateGenerator();
	    generator.GenerateCodeFromCompileUnit(unit, sw, cgOptions);

	    /// get rid of "autogenerated" header
	    StringReader reader = new StringReader(sw.ToString());
	    for(Int32 i=0; i<9; i++) {
		reader.ReadLine();
	    }

	    return reader.ReadToEnd().Trim();
	}

	private ArrayList GetMembers(CodeCompileUnit unit) {
	    ArrayList members = new ArrayList();
	    foreach(CodeNamespace ns in unit.Namespaces) {
		foreach(CodeTypeDeclaration type in ns.Types) {
		    members.AddRange(GetMembers(type, 9999));
		}
	    }

	    return members;
	}

	private ArrayList GetMembers(CodeTypeDeclaration type, int lastLine) {
	    ArrayList members = new ArrayList();
	    Member member = null;
	    foreach(CodeTypeMember m in type.Members) {
		CodeLinePragma line = (CodeLinePragma)m.UserData["Location"];
		if (member != null) {
		    member.LastLine = line.LineNumber - 1;
		    members.Add(member);
		    member = null;
		}
		if (m is CodeMemberField) {
		    member = new Member();
		    member.FirstLine = line.LineNumber;
		    member.Element = m;
		    member.Type = type;
		} else if (m is CodeMemberMethod || m is CodeMemberProperty || m is CodeConstructor || m is CodeTypeConstructor || m is CodeTypeDeclaration) {
		    member = new Member();
		    member.FirstLine = line.LineNumber;
		    member.Element = m;
		    member.Type = type;
		} else {
		    throw new Exception("unknown type member type: " + m.GetType().FullName);
		}
	    }
	    if (member!=null) {
		member.LastLine = lastLine;
		member.IsLastMethod = true;
		members.Add(member);
	    }

	    return members;
	}

	public class Member {
	    public CodeTypeDeclaration Type = null;
	    public CodeTypeMember Element = null;
	    public Int32 FirstLine;
	    public Int32 LastLine;
	    public Boolean HasBeginRegion = false;
	    public Boolean HasEndRegion = false;
	    public String RegionName = String.Empty;
	    public Boolean IsLastMethod = false;

	    public Boolean Generate {
		get {
		    if (Element == null) {
			return false;
		    } else {
			foreach(CodeAttributeDeclaration cad in Element.CustomAttributes) {
			    if (cad.Name.Equals("Generate")) {
				return true;
			    }
			}
			return false;
		    }
		}
	    }
	}


    }
}
