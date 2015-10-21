#region -- License Terms --
//
// MessagePack for CLI
//
// Copyright (C) 2010-2015 FUJIWARA, Yusuke
//
//    Licensed under the Apache License, Version 2.0 (the "License");
//    you may not use this file except in compliance with the License.
//    You may obtain a copy of the License at
//
//        http://www.apache.org/licenses/LICENSE-2.0
//
//    Unless required by applicable law or agreed to in writing, software
//    distributed under the License is distributed on an "AS IS" BASIS,
//    WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//    See the License for the specific language governing permissions and
//    limitations under the License.
//
#endregion -- License Terms --

using System;
using System.Diagnostics;
using System.Diagnostics.Contracts;
using System.Reflection;
using System.Reflection.Emit;
using System.Security;
using System.Threading;

using MsgPack.Serialization.AbstractSerializers;

namespace MsgPack.Serialization.EmittingSerializers
{
	/// <summary>
	///		Defines common features and interfaces for <see cref="SerializationMethodGeneratorManager"/>.
	/// </summary>
	internal sealed class SerializationMethodGeneratorManager
	{
		/// <summary>
		///		Get the appropriate <see cref="SerializationMethodGeneratorManager"/> for the current configuration.
		/// </summary>
		/// <returns>
		///		The appropriate <see cref="SerializationMethodGeneratorManager"/> for the current configuration.
		///		This value will not be <c>null</c>.
		///	</returns>
		public static SerializationMethodGeneratorManager Get()
		{
			return Get( SerializerDebugging.DumpEnabled ? SerializationMethodGeneratorOption.CanDump : SerializationMethodGeneratorOption.Fast );
		}

		/// <summary>
		///		Get the appropriate <see cref="SerializationMethodGeneratorManager"/> for specified options.
		/// </summary>
		/// <param name="option"><see cref="SerializationMethodGeneratorOption"/>.</param>
		/// <returns>
		///		The appropriate <see cref="SerializationMethodGeneratorManager"/> for specified options. 
		///		This value will not be <c>null</c>.
		///	</returns>
		public static SerializationMethodGeneratorManager Get( SerializationMethodGeneratorOption option )
		{
			switch ( option )
			{
				case SerializationMethodGeneratorOption.CanDump:
				{
					return CanDump;
				}
				case SerializationMethodGeneratorOption.CanCollect:
				{
					return CanCollect;
				}
				default:
				{
					return Fast;
				}
			}
		}

		private static readonly ConstructorInfo _debuggableAttributeCtor =
			typeof( DebuggableAttribute ).GetConstructor( new[] { typeof( bool ), typeof( bool ) } );
		private static readonly object[] _debuggableAttributeCtorArguments = { true, true };

		private static int _assemblySequence = -1;

		private static SerializationMethodGeneratorManager _canCollect = new SerializationMethodGeneratorManager( false, true, null );

		/// <summary>
		///		Get the singleton instance for can-collect mode.
		/// </summary>
		public static SerializationMethodGeneratorManager CanCollect
		{
			get { return _canCollect; }
		}

		private static SerializationMethodGeneratorManager _canDump = new SerializationMethodGeneratorManager( true, false, null );

		/// <summary>
		///		Get the singleton instance for can-dump mode.
		/// </summary>
		public static SerializationMethodGeneratorManager CanDump
		{
			get { return _canDump; }
		}

		private static SerializationMethodGeneratorManager _fast = new SerializationMethodGeneratorManager( false, false, null );

		/// <summary>
		///		Get the singleton instance for fast mode.
		/// </summary>
		public static SerializationMethodGeneratorManager Fast
		{
			get { return _fast; }
		}

		internal static void Refresh()
		{
#if !SILVERLIGHT
			_canCollect = new SerializationMethodGeneratorManager( false, true, null );
			_canDump = new SerializationMethodGeneratorManager( true, false, null );
#endif
			_fast = new SerializationMethodGeneratorManager( false, false, null );
		}

#if !WINDOWS_PHONE
		// ReSharper disable once PrivateFieldCanBeConvertedToLocalVariable
		private readonly AssemblyBuilder _assembly;
		private readonly ModuleBuilder _module;
		private readonly bool _isDebuggable;
#endif

#if NETFX_35
		[System.Diagnostics.CodeAnalysis.SuppressMessage( "Microsoft.Usage", "CA1801:ReviewUnusedParameters", MessageId = "isCollectable", Justification = "Used in other platforms" )]
#endif // NETFX_35
#if !NETFX_35
		[SecuritySafeCritical]
#endif // !NETFX_35
		private SerializationMethodGeneratorManager( bool isDebuggable, bool isCollectable, AssemblyBuilder assemblyBuilder )
		{
			this._isDebuggable = isDebuggable;

			string assemblyName;
			if ( assemblyBuilder != null )
			{
				assemblyName = assemblyBuilder.GetName( false ).Name;
				this._assembly = assemblyBuilder;
			}
			else
			{
				assemblyName = typeof( SerializationMethodGeneratorManager ).Namespace + ".GeneratedSerealizers" + Interlocked.Increment( ref _assemblySequence );
				var dedicatedAssemblyBuilder =
					AppDomain.CurrentDomain.DefineDynamicAssembly(
						new AssemblyName( assemblyName ),
						isDebuggable
						? AssemblyBuilderAccess.RunAndSave
#if !NETFX_35
						: ( isCollectable ? AssemblyBuilderAccess.RunAndCollect : AssemblyBuilderAccess.Run )
#else
						: AssemblyBuilderAccess.Run
#endif // !NETFX_35
					);

				SetUpAssemblyBuilderAttributes( dedicatedAssemblyBuilder, isDebuggable );
				this._assembly = dedicatedAssemblyBuilder;
			}

			if ( isDebuggable )
			{
				this._module = this._assembly.DefineDynamicModule( assemblyName, assemblyName + ".dll", true );
			}
			else
			{
				this._module = this._assembly.DefineDynamicModule( assemblyName, true );
			}
		}

		internal static void SetUpAssemblyBuilderAttributes( AssemblyBuilder dedicatedAssemblyBuilder, bool isDebuggable )
		{
			if ( isDebuggable )
			{
				dedicatedAssemblyBuilder.SetCustomAttribute( new CustomAttributeBuilder( _debuggableAttributeCtor, _debuggableAttributeCtorArguments ) );
			}
			else
			{
				dedicatedAssemblyBuilder.SetCustomAttribute(
					new CustomAttributeBuilder(
						// ReSharper disable once AssignNullToNotNullAttribute
						typeof( DebuggableAttribute ).GetConstructor( new[] { typeof( DebuggableAttribute.DebuggingModes ) } ),
						new object[] { DebuggableAttribute.DebuggingModes.IgnoreSymbolStoreSequencePoints }
					)
				);
			}

			dedicatedAssemblyBuilder.SetCustomAttribute(
				new CustomAttributeBuilder(
					// ReSharper disable once AssignNullToNotNullAttribute
					typeof( System.Runtime.CompilerServices.CompilationRelaxationsAttribute ).GetConstructor( new[] { typeof( int ) } ),
					new object[] { 8 }
				)
			);
#if !NETFX_35
			dedicatedAssemblyBuilder.SetCustomAttribute(
				new CustomAttributeBuilder(
					// ReSharper disable once AssignNullToNotNullAttribute
					typeof( SecurityRulesAttribute ).GetConstructor( new[] { typeof( SecurityRuleSet ) } ),
					new object[] { SecurityRuleSet.Level2 },
					new[] { typeof( SecurityRulesAttribute ).GetProperty( "SkipVerificationInFullTrust" ) },
					new object[] { true }
				)
			);
#endif // !NETFX_35
		}

		/// <summary>
		///		Get the dumpable <see cref="SerializationMethodGeneratorManager"/> with specified brandnew assembly builder.
		/// </summary>
		/// <param name="assemblyBuilder">An assembly builder which will store all generated types.</param>
		/// <returns>
		///		The appropriate <see cref="SerializationMethodGeneratorManager"/> to generate pre-cimplied serializers.
		///		This value will not be <c>null</c>.
		///	</returns>
		public static SerializationMethodGeneratorManager Get( AssemblyBuilder assemblyBuilder )
		{
			return new SerializationMethodGeneratorManager( true, false, assemblyBuilder );
		}

		/// <summary>
		///		Creates new <see cref="SerializerEmitter"/> which corresponds to the specified <see cref="EmitterFlavor"/>.
		/// </summary>
		/// <param name="specification">The specification of the serializer.</param>
		/// <param name="baseClass">Type of the base class of the serializer.</param>
		/// <param name="emitterFlavor"><see cref="EmitterFlavor"/>.</param>
		/// <returns>New <see cref="SerializerEmitter"/> which corresponds to the specified <see cref="EmitterFlavor"/>.</returns>
		public SerializerEmitter CreateEmitter( SerializerSpecification specification, Type baseClass, EmitterFlavor emitterFlavor )
		{
			Contract.Requires( specification != null );
			Contract.Requires( baseClass != null );
			Contract.Ensures( Contract.Result<SerializerEmitter>() != null );

			return new SerializerEmitter( this._module, specification, baseClass, this._isDebuggable );
		}

		/// <summary>
		///		Creates new <see cref="EnumSerializerEmitter"/> which corresponds to the specified <see cref="EmitterFlavor"/>.
		/// </summary>
		/// <param name="context">The <see cref="SerializationContext"/>.</param>
		/// <param name="specification">The specification of the serializer.</param>
		/// <param name="emitterFlavor"><see cref="EmitterFlavor"/>.</param>
		/// <returns>New <see cref="EnumSerializerEmitter"/> which corresponds to the specified <see cref="EmitterFlavor"/>.</returns>
		public EnumSerializerEmitter CreateEnumEmitter( SerializationContext context, SerializerSpecification specification, EmitterFlavor emitterFlavor )
		{
			Contract.Requires( context != null );
			Contract.Requires( specification != null );
			Contract.Ensures( Contract.Result<EnumSerializerEmitter>() != null );

			return new EnumSerializerEmitter( context, this._module, specification, this._isDebuggable );
		}
	}
}
