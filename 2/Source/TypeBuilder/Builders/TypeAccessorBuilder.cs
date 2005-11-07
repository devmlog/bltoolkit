using System;
using System.Collections;
using System.Reflection;
using System.Reflection.Emit;

using BLToolkit.Reflection;
using BLToolkit.Reflection.Emit;

namespace BLToolkit.TypeBuilder.Builders
{
	class TypeAccessorBuilder : ITypeBuilder
	{
		public TypeAccessorBuilder(Type type, Type originalType)
		{
			_type         = type;
			_originalType = originalType;
		}

		TypeHelper        _type;
		TypeHelper        _originalType;
		TypeHelper        _accessorType   = new TypeHelper(typeof(TypeAccessor));
		TypeHelper        _memberAccessor = new TypeHelper(typeof(MemberAccessor));
		ArrayList         _nestedTypes    = new ArrayList();
		TypeBuilderHelper _typeBuilder;

		public string AssemblyNameSuffix
		{
			get { return "TypeAccessor"; }
		}

		public Type Build(Type sourceType, AssemblyBuilderHelper assemblyBuilder)
		{
			string typeName = _type.FullName.Replace('+', '.') + ".TypeAccessor";

			Type accessorType = _accessorType;

#if FW2
			accessorType = typeof(TypeAccessor<>).MakeGenericType(new Type[] { _type });
#endif

			_typeBuilder = assemblyBuilder.DefineType(typeName, accessorType);

			_typeBuilder.DefaultConstructor.Emitter
				.ldarg_0
				.call    (TypeHelper.GetDefaultConstructor(accessorType))
				;

			BuildCreateInstanceMethods();
			BuildTypeProperties();
			BuildMembers();

			_typeBuilder.DefaultConstructor.Emitter
				.ret()
				;

			Type result = _typeBuilder.Create();

			foreach (TypeBuilderHelper tb in _nestedTypes)
				tb.Create();

			return result;
		}

		private void BuildCreateInstanceMethods()
		{
			ConstructorInfo baseDefCtor  = _type.GetPublicDefaultConstructor();
			ConstructorInfo baseInitCtor = _type.GetPublicConstructor(typeof(InitContext));

			if (baseDefCtor == null && baseInitCtor == null)
			{
				throw new TypeBuilderException(
					string.Format("The '{0}' type must have public default or init constructor.", _type.Name));
			}

			// CreateInstance.
			//
			MethodBuilderHelper method = _typeBuilder.DefineMethod(
				TypeHelper.GetMethodNoGeneric(_accessorType, "CreateInstance", Type.EmptyTypes));

			if (baseDefCtor != null)
			{
				method.Emitter
					.newobj (baseDefCtor)
					.ret()
					;
			}
			else
			{
				method.Emitter
					.ldnull
					.newobj (baseInitCtor)
					.ret()
					;
			}

			// CreateInstance(IniContext).
			//
			method = _typeBuilder.DefineMethod(
				TypeHelper.GetMethodNoGeneric(_accessorType, "CreateInstance", typeof(InitContext)));

			if (baseInitCtor != null)
			{
				method.Emitter
					.ldarg_1
					.newobj (baseInitCtor)
					.ret()
					;
			}
			else
			{
				method.Emitter
					.newobj (baseDefCtor)
					.ret()
					;
			}
		}

		private void BuildTypeProperties()
		{
			// Type.
			//
			MethodBuilderHelper method =
				_typeBuilder.DefineMethod(_accessorType.GetProperty("Type").GetGetMethod());

			method.Emitter
				.LoadType(_type)
				.ret()
				;

			// OriginalType.
			//
			method = 
				_typeBuilder.DefineMethod(_accessorType.GetProperty("OriginalType").GetGetMethod());

			method.Emitter
				.LoadType(_originalType)
				.ret()
				;
		}

		private void BuildMembers()
		{
			foreach (MemberInfo mi in _type.GetFields())
				BuildMember(mi);

			foreach (PropertyInfo pi in _type.GetProperties())
				if (pi.GetIndexParameters().Length == 0)
					BuildMember(pi);
		}

		private void BuildMember(MemberInfo mi)
		{
			TypeBuilderHelper nestedType = _typeBuilder.DefineNestedType(
				"Accessor$" + mi.Name, TypeAttributes.NestedPrivate, typeof(MemberAccessor));

			ConstructorBuilderHelper ctorBuilder = BuildNestedTypeConstructor(nestedType, mi);

			BuildInitMember(mi, ctorBuilder);
			BuildGetter    (mi, nestedType);
			BuildSetter    (mi, nestedType);

			// FW 1.1 wants nested types to be created before parent.
			//
			_nestedTypes.Add(nestedType);
		}

		private void BuildInitMember(MemberInfo mi, ConstructorBuilderHelper ctorBuilder)
		{
			_typeBuilder.DefaultConstructor.Emitter
				.ldarg_0
				.ldarg_0
				.ldc_i4  (mi is FieldInfo? 1: 2)
				.ldstr   (mi.Name)
				.call    (_accessorType.GetMethod("GetMember", typeof(int), typeof(string)))
				.newobj  (ctorBuilder)
				.call    (_accessorType.GetMethod("AddMember", typeof(MemberAccessor)))
				;
		}

		private void BuildGetter(MemberInfo mi, TypeBuilderHelper nestedType)
		{
			MethodInfo getMethod = null;

			if (mi is PropertyInfo)
			{
				getMethod = ((PropertyInfo)mi).GetGetMethod();

				if (getMethod == null)
				{
					if (_type != _originalType)
						getMethod = _type.GetMethod("get_" + mi.Name);

					if (getMethod == null)
						return;
				}
			}

			MethodBuilderHelper method = nestedType.DefineMethod(
				_memberAccessor.GetMethod("GetValue", typeof(object)));
			
			EmitHelper emit = method.Emitter;

			emit
				.ldarg_1
				.castclass (_type)
				.end();

			if (mi is FieldInfo)
			{
				FieldInfo fi = (FieldInfo)mi;

				emit
					.ldfld          (fi)
					.boxIfValueType (fi.FieldType)
					;
			}
			else
			{
				PropertyInfo pi = (PropertyInfo)mi;

				emit
					.callvirt       (getMethod)
					.boxIfValueType (pi.PropertyType)
					;
			}

			emit
				.ret()
				;

			nestedType.DefineMethod(_memberAccessor.GetProperty("HasGetter").GetGetMethod()).Emitter
				.ldc_i4_1
				.ret()
				;
		}

		private void BuildSetter(MemberInfo mi, TypeBuilderHelper nestedType)
		{
			MethodInfo setMethod = null;

			if (mi is PropertyInfo)
			{
				setMethod = ((PropertyInfo)mi).GetSetMethod();

				if (setMethod == null)
				{
					if (_type != _originalType)
						setMethod = _type.GetMethod("set_" + mi.Name);

					if (setMethod == null)
						return;
				}
			}

			MethodBuilderHelper method = nestedType.DefineMethod(
				_memberAccessor.GetMethod("SetValue", typeof(object), typeof(object)));
			
			EmitHelper emit = method.Emitter;

			emit
				.ldarg_1
				.castclass (_type)
				.ldarg_2
				.end();

			if (mi is FieldInfo)
			{
				FieldInfo fi = (FieldInfo)mi;

				emit
					.CastFromObject (fi.FieldType)
					.stfld          (fi)
					;
			}
			else
			{
				PropertyInfo pi = (PropertyInfo)mi;

				emit
					.CastFromObject (pi.PropertyType)
					.callvirt       (setMethod)
					;
			}

			emit
				.ret()
				;

			nestedType.DefineMethod(_memberAccessor.GetProperty("HasSetter").GetGetMethod()).Emitter
				.ldc_i4_1
				.ret()
				;
		}

		private ConstructorBuilderHelper BuildNestedTypeConstructor(
			TypeBuilderHelper nestedType, MemberInfo mi)
		{
			ConstructorBuilderHelper ctorBuilder = nestedType.DefinePublicConstructor(typeof(MemberInfo));

			ctorBuilder.Emitter
				.ldarg_0
				.ldarg_1
				.call    (TypeHelper.GetConstructor(typeof(MemberAccessor), typeof(MemberInfo)))
				.ret()
				;

			return ctorBuilder;
		}
	}
}
