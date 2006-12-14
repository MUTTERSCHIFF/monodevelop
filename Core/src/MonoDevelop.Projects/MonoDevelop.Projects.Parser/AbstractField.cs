// <file>
//     <copyright see="prj:///doc/copyright.txt"/>
//     <license see="prj:///doc/license.txt"/>
//     <owner name="Mike Krüger" email="mike@icsharpcode.net"/>
//     <version value="$version"/>
// </file>
using System;
using MonoDevelop.Projects.Utility;
using System.Reflection;

namespace MonoDevelop.Projects.Parser {
	[Serializable]
	public class DefaultField : AbstractMember, IField
	{
		public DefaultField ()
		{
		}
		
		public DefaultField (IClass declaringType, IReturnType type, string fullyQualifiedName, ModifierEnum m, IRegion region)
		{
			this.returnType = type;
			this.FullyQualifiedName = fullyQualifiedName;
			this.region = region;
			this.declaringType = declaringType;
			modifiers = m;
		}
		
		public override int CompareTo(object ob) 
		{
			IField field = (IField) ob;		// Just crash if this is not a field
			return base.CompareTo (field);
		}
		
		public override bool Equals (object ob)
		{
			IField other = ob as IField;
			if (other == null) return false;
			return CompareTo (other) == 0;
		}
		
		public override int GetHashCode ()
		{
			return base.GetHashCode ();
		}
	}
}
