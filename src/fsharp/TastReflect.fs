﻿namespace Microsoft.FSharp.Compiler

#nowarn "40"
#if !NO_EXTENSIONTYPING

module internal TastReflect =


    open System
    open System.IO
    open System.Collections.Generic
    open System.Reflection
    open Microsoft.FSharp.Compiler.Range
    open Microsoft.FSharp.Compiler.Tast
    open Microsoft.FSharp.Compiler.Tastops
    open Microsoft.FSharp.Compiler.AbstractIL.IL
    open Microsoft.FSharp.Core.CompilerServices
    open Microsoft.FSharp.Compiler.TcGlobals
    open System.Runtime.Remoting.Lifetime
    open System.Runtime.InteropServices.ComTypes


    [<AutoOpen>]
    module Utils = 
        let nullToOption x = match x with null -> None | _ -> Some x
        let optionToNull x = match x with None -> null | Some x -> x

        let notRequired msg = 
           failwith (sprintf "SHOULD NOT BE REQUIRED! %s. Stack trace:\n%s" msg (System.Diagnostics.StackTrace().ToString()))

        // A table tracking how wrapped type definition objects are translated to cloned objects.
        // Unique wrapped type definition objects must be translated to unique wrapper objects, based 
        // on object identity.
        type TxTable<'T2>() = 
            let tab = Dictionary<Stamp, 'T2>()
            member __.Get inp f = 
                if tab.ContainsKey inp then 
                    tab.[inp] 
                else 
                    let res = f() 
                    tab.[inp] <- res
                    res

            member __.ContainsKey inp = tab.ContainsKey inp 
            member __.Values = tab.Values

        let lengthsEqAndForall2 (arr1: 'T1[]) (arr2: 'T2[]) f = 
            (arr1.Length = arr2.Length) &&
            (arr1,arr2) ||> Array.forall2 f

        // Instantiate a type's generic parameters
        let rec instType inst (ty:Type) = 
            if ty.IsGenericType then 
                let args = Array.map (instType inst) (ty.GetGenericArguments())
                ty.GetGenericTypeDefinition().MakeGenericType(args)
            elif ty.HasElementType then 
                let ety = instType inst (ty.GetElementType()) 
                if ty.IsArray then 
                    let rank = ty.GetArrayRank()
                    if rank = 1 then ety.MakeArrayType()
                    else ety.MakeArrayType(rank)
                elif ty.IsPointer then ety.MakePointerType()
                elif ty.IsByRef then ety.MakeByRefType()
                else ty
            elif ty.IsGenericParameter then 
                let pos = ty.GenericParameterPosition
                let (inst1: Type[], inst2: Type[]) = inst 
                if pos < inst1.Length then inst1.[pos]
                elif pos < inst1.Length + inst2.Length then inst2.[pos - inst1.Length]
                else ty
            else ty

        let instParameterInfo inst (inp: ParameterInfo) = 
            { new ParameterInfo() with 
                override __.Name = inp.Name 
                override __.ParameterType = inp.ParameterType |> instType inst
                override __.Attributes = inp.Attributes
                override __.RawDefaultValue = inp.RawDefaultValue
                override __.GetCustomAttributesData() = inp.GetCustomAttributesData()
                override x.ToString() = inp.ToString() + "@inst" }

        let rec eqType (ty1:Type) (ty2:Type) = 
            if ty1.IsGenericType then ty2.IsGenericType && lengthsEqAndForall2 (ty1.GetGenericArguments()) (ty2.GetGenericArguments()) eqType
            elif ty1.IsArray then ty2.IsArray && ty1.GetArrayRank() = ty2.GetArrayRank() && eqType (ty1.GetElementType()) (ty2.GetElementType()) 
            elif ty1.IsPointer then ty2.IsPointer && eqType (ty1.GetElementType()) (ty2.GetElementType()) 
            elif ty1.IsByRef then ty2.IsByRef && eqType (ty1.GetElementType()) (ty2.GetElementType()) 
            else ty1.Equals(box ty2)

        //let hashILParameterTypes (ps: ILParameters) = 
        //   // This hash code doesn't need to be very good as hashing by name is sufficient to give decent hash granularity
        //   ps.Length 

        //let eqAssemblyAndCcu (_ass1: Assembly) (_sco2: ILScopeRef) = 
        //    true // TODO (though omitting this is not a problem in practice since type equivalence by name is sufficient to bind methods)


        //let rec eqTypeAndTyconRef (ty1: Type) (ty2: ILTypeRef) = 
        //    ty1.Name = ty2.Name && 
        //    ty1.Namespace = (uoptionToNull ty2.Namespace) &&
        //    match ty2.Scope with 
        //    | ILTypeRefScope.Top scoref2 -> eqAssemblyAndCcu ty1.Assembly scoref2
        //    | ILTypeRefScope.Nested tref2 -> ty1.IsNested && eqTypeAndTyconRef ty1.DeclaringType tref2

        //let rec eqTypesAndTTypes (tys1: Type[]) (tys2: ILType[]) = 
        //    eqTypesAndTTypesWithInst [| |] tys1 tys2 

        //and eqTypesAndTTypesWithInst inst2 (tys1: Type[]) (tys2: ILType[]) = 
        //    lengthsEqAndForall2 tys1 tys2 (eqTypeAndTTypeWithInst inst2)

        //and eqTypeAndTTypeWithInst inst2 (ty1: Type) (ty2: ILType) = 
        //    match ty2 with 
        //    | (ILType.Value(tspec2) | ILType.Boxed(tspec2))->
        //        if tspec2.GenericArgs.Length > 0 then 
        //            ty1.IsGenericType && eqTypeAndTyconRef (ty1.GetGenericTypeDefinition()) tspec2.TypeRef && eqTypesAndTTypesWithInst inst2 (ty1.GetGenericArguments()) tspec2.GenericArgs
        //        else 
        //            not ty1.IsGenericType && eqTypeAndTyconRef ty1 tspec2.TypeRef
        //    | ILType.Array(rank2, arg2) ->
        //        ty1.IsArray && ty1.GetArrayRank() = rank2.Rank && eqTypeAndTTypeWithInst inst2 (ty1.GetElementType()) arg2
        //    | ILType.Ptr(arg2) -> 
        //        ty1.IsPointer && eqTypeAndTTypeWithInst inst2 (ty1.GetElementType()) arg2
        //    | ILType.Byref(arg2) ->
        //        ty1.IsByRef && eqTypeAndTTypeWithInst inst2 (ty1.GetElementType()) arg2
        //    | ILType.Var(arg2) ->
        //        if int arg2 < inst2.Length then 
        //             eqType ty1 inst2.[int arg2]  
        //        else
        //             ty1.IsGenericParameter && ty1.GenericParameterPosition = int arg2
                    
        //    | _ -> false

        //let eqParametersAndILParameterTypesWithInst inst2 (ps1: ParameterInfo[])  (ps2: ILParameters) = 
        //    lengthsEqAndForall2 ps1 ps2 (fun p1 p2 -> eqTypeAndTTypeWithInst inst2 p1.ParameterType p2.ParameterType)

        //let adjustTypeAttributes isNested attributes = 
        //    let visibilityAttributes = 
        //        match attributes &&& TypeAttributes.VisibilityMask with 
        //        | TypeAttributes.Public when isNested -> TypeAttributes.NestedPublic
        //        | TypeAttributes.NotPublic when isNested -> TypeAttributes.NestedAssembly
        //        | TypeAttributes.NestedPublic when not isNested -> TypeAttributes.Public
        //        | TypeAttributes.NestedAssembly 
        //        | TypeAttributes.NestedPrivate 
        //        | TypeAttributes.NestedFamORAssem
        //        | TypeAttributes.NestedFamily
        //        | TypeAttributes.NestedFamANDAssem when not isNested -> TypeAttributes.NotPublic
        //        | a -> a
        //    (attributes &&& ~~~TypeAttributes.VisibilityMask) ||| visibilityAttributes



        //let convFieldInit x = 
        //    match x with 
        //    | ILFieldInit.String s       -> box s
        //    | ILFieldInit.Bool bool      -> box bool   
        //    | ILFieldInit.Char u16       -> box (char (int u16))  
        //    | ILFieldInit.Int8 i8        -> box i8     
        //    | ILFieldInit.Int16 i16      -> box i16    
        //    | ILFieldInit.Int32 i32      -> box i32    
        //    | ILFieldInit.Int64 i64      -> box i64    
        //    | ILFieldInit.UInt8 u8       -> box u8     
        //    | ILFieldInit.UInt16 u16     -> box u16    
        //    | ILFieldInit.UInt32 u32     -> box u32    
        //    | ILFieldInit.UInt64 u64     -> box u64    
        //    | ILFieldInit.Single ieee32 -> box ieee32 
        //    | ILFieldInit.Double ieee64 -> box ieee64 
        //    | ILFieldInit.Null            -> (null :> Object)


    /// Represents the type constructor in a provided symbol type.
    [<RequireQualifiedAccess>]
    type ReflectTypeSymbolKind = 
        | SDArray 
        | Array of int 
        | Pointer 
        | ByRef 
        | Generic of ReflectTypeDefinition


    /// Represents an array or other symbolic type involving a provided type as the argument.
    /// See the type provider spec for the methods that must be implemented.
    /// Note that the type provider specification does not require us to implement pointer-equality for provided types.
    and ReflectTypeSymbol(kind: ReflectTypeSymbolKind, args: Type[]) =
        inherit Type()

        let notRequired msg = 
            System.Diagnostics.Debugger.Break()
            failwith ("not required: " + msg)

        override __.FullName =   
            match kind,args with 
            | ReflectTypeSymbolKind.SDArray,[| arg |] -> arg.FullName + "[]" 
            | ReflectTypeSymbolKind.Array _,[| arg |] -> arg.FullName + "[*]" 
            | ReflectTypeSymbolKind.Pointer,[| arg |] -> arg.FullName + "*" 
            | ReflectTypeSymbolKind.ByRef,[| arg |] -> arg.FullName + "&"
            | ReflectTypeSymbolKind.Generic gtd, args -> gtd.FullName + "[" + (args |> Array.map (fun arg -> "[" + arg.AssemblyQualifiedName + "]") |> String.concat ",") + "]"
            | _ -> failwith "unreachable"

        override __.DeclaringType =                                                                 
            match kind,args with 
            | ReflectTypeSymbolKind.SDArray,[| arg |]
            | ReflectTypeSymbolKind.Array _,[| arg |] 
            | ReflectTypeSymbolKind.Pointer,[| arg |] 
            | ReflectTypeSymbolKind.ByRef,[| arg |] -> arg.DeclaringType
            | ReflectTypeSymbolKind.Generic gtd,_ -> gtd.DeclaringType
            | _ -> failwith "unreachable"

        override __.IsAssignableFrom(otherTy) = 
            match kind with
            | ReflectTypeSymbolKind.Generic gtd ->
                if otherTy.IsGenericType then
                    let otherGtd = otherTy.GetGenericTypeDefinition()
                    let otherArgs = otherTy.GetGenericArguments()
                    let yes = gtd.Equals(otherGtd) && Seq.forall2 eqType args otherArgs
                    yes
                else
                    base.IsAssignableFrom(otherTy)
            | _ -> base.IsAssignableFrom(otherTy)

        override this.IsSubclassOf(otherTy) = 
            base.IsSubclassOf(otherTy) ||
            match kind with
            | ReflectTypeSymbolKind.Generic gtd -> 
                let md : TyconRef = gtd.Metadata
                md.IsFSharpDelegateTycon && (otherTy = typeof<Delegate>) // F# quotations implementation
            | _ -> false

        override __.Name =
            match kind,args with 
            | ReflectTypeSymbolKind.SDArray,[| arg |] -> arg.Name + "[]" 
            | ReflectTypeSymbolKind.Array _,[| arg |] -> arg.Name + "[*]" 
            | ReflectTypeSymbolKind.Pointer,[| arg |] -> arg.Name + "*" 
            | ReflectTypeSymbolKind.ByRef,[| arg |] -> arg.Name + "&"
            | ReflectTypeSymbolKind.Generic gtd, _args -> gtd.Name 
            | _ -> failwith "unreachable"

        override __.BaseType =
            match kind with 
            | ReflectTypeSymbolKind.SDArray -> typeof<System.Array>
            | ReflectTypeSymbolKind.Array _ -> typeof<System.Array>
            | ReflectTypeSymbolKind.Pointer -> typeof<System.ValueType>
            | ReflectTypeSymbolKind.ByRef -> typeof<System.ValueType>
            | ReflectTypeSymbolKind.Generic gtd  -> 
                if gtd.BaseType = null
                then null 
                else instType (args, [| |]) gtd.BaseType
            
        override this.Assembly = 
            match kind, args with 
            | ReflectTypeSymbolKind.SDArray,[| arg |] 
            | ReflectTypeSymbolKind.Array _,[| arg |] 
            | ReflectTypeSymbolKind.Pointer,[| arg |] 
            | ReflectTypeSymbolKind.ByRef,[| arg |] -> arg.Assembly
            | ReflectTypeSymbolKind.Generic gtd, _ -> gtd.Assembly
            | _ -> notRequired "Assembly" this.Name

        override this.Namespace = 
            match kind, args with 
            | ReflectTypeSymbolKind.SDArray,[| arg |] 
            | ReflectTypeSymbolKind.Array _,[| arg |] 
            | ReflectTypeSymbolKind.Pointer,[| arg |] 
            | ReflectTypeSymbolKind.ByRef,[| arg |] -> arg.Namespace
            | ReflectTypeSymbolKind.Generic gtd, _ -> gtd.Namespace 
            | _ -> failwith "unreachable"

        override __.GetArrayRank() = (match kind with ReflectTypeSymbolKind.Array n -> n | ReflectTypeSymbolKind.SDArray -> 1 | _ -> invalidOp "non-array type")
        override __.IsValueTypeImpl() = (match kind with ReflectTypeSymbolKind.Generic gtd -> gtd.IsValueType | _ -> false)
        override __.IsArrayImpl() = (match kind with ReflectTypeSymbolKind.Array _ | ReflectTypeSymbolKind.SDArray -> true | _ -> false)
        override __.IsByRefImpl() = (match kind with ReflectTypeSymbolKind.ByRef _ -> true | _ -> false)
        override __.IsPointerImpl() = (match kind with ReflectTypeSymbolKind.Pointer _ -> true | _ -> false)
        override __.IsPrimitiveImpl() = false
        override __.IsGenericType = (match kind with ReflectTypeSymbolKind.Generic _ -> true | _ -> false)
        override __.GetGenericArguments() = (match kind with ReflectTypeSymbolKind.Generic _ -> args | _ -> [| |])
        override __.GetGenericTypeDefinition() = (match kind with ReflectTypeSymbolKind.Generic e -> (e :> Type) | _ -> invalidOp "non-generic type")
        override __.IsCOMObjectImpl() = false
        override __.HasElementTypeImpl() = (match kind with ReflectTypeSymbolKind.Generic _ -> false | _ -> true)
        override __.GetElementType() = (match kind,args with (ReflectTypeSymbolKind.Array _  | ReflectTypeSymbolKind.SDArray | ReflectTypeSymbolKind.ByRef | ReflectTypeSymbolKind.Pointer),[| e |] -> e | _ -> invalidOp (sprintf "%+A, %+A: not an array, pointer or byref type" kind args))

        override this.Module : Module = notRequired "Module" this.Name

        override this.GetHashCode()                                                                    = 
            match kind,args with 
            | ReflectTypeSymbolKind.SDArray,[| arg |] -> 10 + hash arg
            | ReflectTypeSymbolKind.Array _,[| arg |] -> 163 + hash arg
            | ReflectTypeSymbolKind.Pointer,[| arg |] -> 283 + hash arg
            | ReflectTypeSymbolKind.ByRef,[| arg |] -> 43904 + hash arg
            | ReflectTypeSymbolKind.Generic gtd,_ -> 9797 + hash gtd + Array.sumBy hash args
            | _ -> failwith "unreachable"
        
        override this.Equals(other: obj) =
            printfn "EQUALS"
            match other with
            | :? ReflectTypeSymbol as otherTy -> (kind, args) = (otherTy.Kind, otherTy.Args)
            | _ -> false

        member this.Kind = kind
        member this.Args = args
        
        override this.GetConstructors _bindingAttr =
            match kind,args with 
            | ReflectTypeSymbolKind.SDArray,[| arg |] 
            | ReflectTypeSymbolKind.Array _,[| arg |] 
            | ReflectTypeSymbolKind.Pointer,[| arg |]         
            | ReflectTypeSymbolKind.ByRef,[| arg |] -> arg.GetConstructors(_bindingAttr)
            | ReflectTypeSymbolKind.Generic gtd, _ -> gtd.GetConstructors(_bindingAttr)
            | _ -> failwith "unreachable"

        override this.GetMethodImpl(_ (* name *), _bindingAttr, _binderBinder, _callConvention, _ (* types *), _modifiers) = notRequired "ReflectTypeSymbol: GetMethodImpl" this.Name
            //match kind with
            //| ReflectTypeSymbolKind.Generic gtd -> 

            //    let md = 
            //        match types with 
            //        | null -> 
            //            match gtd.Metadata.Methods.FindByName(name) with 
            //            | [| md |] -> md
            //            | [| |] -> failwith (sprintf "method %s not found" name)
            //            | _ -> failwith (sprintf "multiple methods called '%s' found" name)
            //        | _ -> 
            //            match gtd.Metadata.Methods.FindByNameAndArity(name, types.Length) with
            //            | [| |] ->  failwith (sprintf "method %s not found with arity %d" name types.Length)
            //            | mds -> 
            //                match mds |> Array.filter (fun md -> eqTypesAndTTypesWithInst args types md.ParameterTypes) with 
            //                | [| |] -> 
            //                    let md1 = mds.[0]
            //                    ignore md1
            //                    failwith (sprintf "no method %s with arity %d found with right types. Comparisons:" name types.Length
            //                              + ((types, md1.ParameterTypes) ||> Array.map2 (fun a pt -> eqTypeAndTTypeWithInst args a pt |> sprintf "%+A") |> String.concat "\n"))
            //                | [| md |] -> md
            //                | _ -> failwith (sprintf "multiple methods %s with arity %d found with right types" name types.Length)

            //    gtd.MakeMethodInfo (this, md)

            //| _ -> notRequired "ReflectTypeSymbol: GetMethodImpl" this.Name

        override this.GetConstructorImpl(_bindingAttr, _binderBinder, _callConvention, _ (* types *), _modifiers) = notRequired "ReflectTypeSymbol: GetConstructorImpl" this.Name
            //match kind with
            //| ReflectTypeSymbolKind.Generic gtd -> 
            //    let name = ".ctor"
            //    let md = 
            //        match types with 
            //        | null -> 
            //            match gtd.Metadata.Methods.FindByName(name) with 
            //            | [| md |] -> md
            //            | [| |] -> failwith (sprintf "method %s not found" name)
            //            | _ -> failwith (sprintf "multiple methods called '%s' found" name)
            //        | _ -> 
            //            gtd.Metadata.Methods.FindByNameAndArity(name, types.Length)
            //            |> Array.find (fun md -> eqTypesAndTTypesWithInst types args md.ParameterTypes)
            //    gtd.MakeConstructorInfo (this, md)

            //| _ -> notRequired "ReflectTypeSymbol: GetConstructorImpl" this.Name

        override this.AssemblyQualifiedName                                                            = this.FullName + ", " + this.Assembly.FullName

        override this.GetMembers _bindingAttr =
            match kind,args with 
            | ReflectTypeSymbolKind.SDArray,[| arg |] 
            | ReflectTypeSymbolKind.Array _,[| arg |] 
            | ReflectTypeSymbolKind.Pointer,[| arg |]         
            | ReflectTypeSymbolKind.ByRef,[| arg |] -> arg.GetMembers(_bindingAttr)
            | ReflectTypeSymbolKind.Generic gtd, _ -> gtd.GetMembers(_bindingAttr)
            | _ -> failwith "unreachable"

        override this.GetMethods _bindingAttr =
            match kind,args with 
            | ReflectTypeSymbolKind.SDArray,[| arg |] 
            | ReflectTypeSymbolKind.Array _,[| arg |] 
            | ReflectTypeSymbolKind.Pointer,[| arg |]         
            | ReflectTypeSymbolKind.ByRef,[| arg |] -> arg.GetMethods(_bindingAttr)
            | ReflectTypeSymbolKind.Generic gtd, _ -> gtd.GetMethods(_bindingAttr)
            | _ -> failwith "unreachable"

        override this.GetField(_name, _bindingAttr) =
            match kind,args with 
            | ReflectTypeSymbolKind.SDArray,[| arg |] 
            | ReflectTypeSymbolKind.Array _,[| arg |] 
            | ReflectTypeSymbolKind.Pointer,[| arg |]         
            | ReflectTypeSymbolKind.ByRef,[| arg |] -> arg.GetField(_name,_bindingAttr)
            | ReflectTypeSymbolKind.Generic gtd, _ -> gtd.GetField(_name,_bindingAttr)
            | _ -> failwith "unreachable"

        override this.GetFields _bindingAttr =
            match kind,args with 
            | ReflectTypeSymbolKind.SDArray,[| arg |] 
            | ReflectTypeSymbolKind.Array _,[| arg |] 
            | ReflectTypeSymbolKind.Pointer,[| arg |]         
            | ReflectTypeSymbolKind.ByRef,[| arg |] -> arg.GetFields(_bindingAttr)
            | ReflectTypeSymbolKind.Generic gtd, _ -> gtd.GetFields(_bindingAttr)
            | _ -> failwith "unreachable"

        override this.GetInterface(_name, _ignoreCase) =
            match kind,args with 
            | ReflectTypeSymbolKind.SDArray,[| arg |] 
            | ReflectTypeSymbolKind.Array _,[| arg |] 
            | ReflectTypeSymbolKind.Pointer,[| arg |]         
            | ReflectTypeSymbolKind.ByRef,[| arg |] -> arg.GetInterface(_name, _ignoreCase)
            | ReflectTypeSymbolKind.Generic gtd, _ -> gtd.GetInterface(_name, _ignoreCase)
            | _ -> failwith "unreachable"

        override this.GetInterfaces() =
            match kind,args with 
            | ReflectTypeSymbolKind.SDArray,[| arg |] 
            | ReflectTypeSymbolKind.Array _,[| arg |] 
            | ReflectTypeSymbolKind.Pointer,[| arg |]         
            | ReflectTypeSymbolKind.ByRef,[| arg |] -> arg.GetInterfaces()
            | ReflectTypeSymbolKind.Generic gtd, _ -> gtd.GetInterfaces()
            | _ -> failwith "unreachable"

        override this.GetEvent(_name, _bindingAttr) =
            match kind,args with 
            | ReflectTypeSymbolKind.SDArray,[| arg |] 
            | ReflectTypeSymbolKind.Array _,[| arg |] 
            | ReflectTypeSymbolKind.Pointer,[| arg |]         
            | ReflectTypeSymbolKind.ByRef,[| arg |] -> arg.GetEvent(_name, _bindingAttr)
            | ReflectTypeSymbolKind.Generic gtd, _ -> gtd.GetEvent(_name, _bindingAttr)
            | _ -> failwith "unreachable"

        override this.GetEvents _bindingAttr =
            match kind,args with 
            | ReflectTypeSymbolKind.SDArray,[| arg |] 
            | ReflectTypeSymbolKind.Array _,[| arg |] 
            | ReflectTypeSymbolKind.Pointer,[| arg |]         
            | ReflectTypeSymbolKind.ByRef,[| arg |] -> arg.GetEvents(_bindingAttr)
            | ReflectTypeSymbolKind.Generic gtd, _ -> gtd.GetEvents(_bindingAttr)
            | _ -> failwith "unreachable"

        override this.GetProperties _bindingAttr =
            match kind,args with 
            | ReflectTypeSymbolKind.SDArray,[| arg |] 
            | ReflectTypeSymbolKind.Array _,[| arg |] 
            | ReflectTypeSymbolKind.Pointer,[| arg |]         
            | ReflectTypeSymbolKind.ByRef,[| arg |] -> arg.GetProperties(_bindingAttr)
            | ReflectTypeSymbolKind.Generic gtd, _ -> gtd.GetProperties(_bindingAttr)
            | _ -> failwith "unreachable"

        override this.GetPropertyImpl(_name, _bindingAttr, _binder, _returnType, _types, _modifiers)    = notRequired "GetPropertyImpl" this.Name
        override this.GetNestedTypes _bindingAttr =
            match kind,args with 
            | ReflectTypeSymbolKind.SDArray,[| arg |] 
            | ReflectTypeSymbolKind.Array _,[| arg |] 
            | ReflectTypeSymbolKind.Pointer,[| arg |]         
            | ReflectTypeSymbolKind.ByRef,[| arg |] -> arg.GetNestedTypes(_bindingAttr)
            | ReflectTypeSymbolKind.Generic gtd, _ -> gtd.GetNestedTypes(_bindingAttr)
            | _ -> failwith "unreachable"

        override this.GetNestedType(_name, _bindingAttr) =
            match kind,args with 
            | ReflectTypeSymbolKind.SDArray,[| arg |] 
            | ReflectTypeSymbolKind.Array _,[| arg |] 
            | ReflectTypeSymbolKind.Pointer,[| arg |]         
            | ReflectTypeSymbolKind.ByRef,[| arg |] -> arg.GetNestedType(_name, _bindingAttr)
            | ReflectTypeSymbolKind.Generic gtd, _ -> gtd.GetNestedType(_name, _bindingAttr)
            | _ -> failwith "unreachable"

        override this.GetAttributeFlagsImpl() =
            match kind,args with 
            | ReflectTypeSymbolKind.SDArray,[| arg |] 
            | ReflectTypeSymbolKind.Array _,[| arg |] 
            | ReflectTypeSymbolKind.Pointer,[| arg |]         
            | ReflectTypeSymbolKind.ByRef,[| arg |] -> arg.Attributes
            | ReflectTypeSymbolKind.Generic gtd, _ -> gtd.Attributes
            | _ -> failwith "unreachable"
        
        override this.UnderlyingSystemType = (this :> Type)

        override this.GetCustomAttributesData() =
            match kind with
            | ReflectTypeSymbolKind.Generic gtd -> gtd.GetCustomAttributesData()
            | _ -> [| |] :> _

        override this.MemberType                                                                       = notRequired "MemberType" this.Name
        override this.GetMember(_name,_mt,_bindingAttr)                                                = notRequired "GetMember" this.Name
        override this.GUID                                                                             = notRequired "GUID" this.Name
        override this.InvokeMember(_name, _invokeAttr, _binder, _target, _args, _modifiers, _culture, _namedParameters) = notRequired "InvokeMember" this.Name
        override this.GetCustomAttributes(_inherit)                                                    = [| |]
        override this.GetCustomAttributes(_attributeType, _inherit)                                    = [| |]
        override this.IsDefined(_attributeType, _inherit)                                              = false
        override this.MakeArrayType() = ReflectTypeSymbol(ReflectTypeSymbolKind.SDArray, [| this |]) :> Type
        override this.MakeArrayType arg = ReflectTypeSymbol(ReflectTypeSymbolKind.Array arg, [| this |]) :> Type
        override this.MakePointerType() = ReflectTypeSymbol(ReflectTypeSymbolKind.Pointer, [| this |]) :> Type
        override this.MakeByRefType() = ReflectTypeSymbol(ReflectTypeSymbolKind.ByRef, [| this |]) :> Type

        override this.ToString() = this.FullName

    and ReflectMethodSymbol(gmd: MethodInfo, gargs: Type[]) =
        inherit MethodInfo() 

        override __.Attributes        = gmd.Attributes
        override __.Name              = gmd.Name
        override __.DeclaringType     = gmd.DeclaringType
        override __.MemberType        = gmd.MemberType

        override __.GetParameters()   = gmd.GetParameters() |> Array.map (instParameterInfo (gmd.DeclaringType.GetGenericArguments(), gargs))
        override __.CallingConvention = gmd.CallingConvention
        override __.ReturnType        = gmd.ReturnType |> instType (gmd.DeclaringType.GetGenericArguments(), gargs)
        override __.IsGenericMethod   = true
        override __.GetGenericArguments() = gargs
        override __.MetadataToken = gmd.MetadataToken

        override __.GetCustomAttributesData() = gmd.GetCustomAttributesData()

        override __.GetHashCode() = gmd.GetHashCode()
        override this.Equals(that:obj) = 
            match that with 
            | :? MethodInfo as thatMI -> thatMI.IsGenericMethod && gmd.Equals(thatMI.GetGenericMethodDefinition()) && lengthsEqAndForall2 (gmd.GetGenericArguments()) (thatMI.GetGenericArguments()) (=)
            | _ -> false

        override __.MethodHandle = notRequired "MethodHandle"
        override __.ReturnParameter   = notRequired "ReturnParameter" 
        override __.IsDefined(_attributeType, _inherited)                   = notRequired "IsDefined"
        override __.ReturnTypeCustomAttributes                            = notRequired "ReturnTypeCustomAttributes"
        override __.GetBaseDefinition()                                   = notRequired "GetBaseDefinition"
        override __.GetMethodImplementationFlags()                        = notRequired "GetMethodImplementationFlags"
        override __.Invoke(_obj, _invokeAttr, _binder, _parameters, _culture)  = notRequired "Invoke"
        override __.ReflectedType                                         = notRequired "ReflectedType"
        override __.GetCustomAttributes(_inherited)                        = notRequired "GetCustomAttributes"
        override __.GetCustomAttributes(_attributeType, _inherited)         = notRequired "GetCustomAttributes"

        override __.ToString() = gmd.ToString() + "@inst"

    and ReflectGenericParam(asm, pos, inp: Typar) =
        inherit Type() with 
        override __.Name = inp.Name 
        override __.Assembly = (asm :> Assembly)
        override __.FullName = inp.Name
        override __.IsGenericParameter = true
        override __.GenericParameterPosition = pos
        override __.GetGenericParameterConstraints() = [||]
            //TODO: Implement generic parameter constraints
            //inp.Constraints |> Array.map (fun x -> x TxILType (gpsf()))
                        
        override __.MemberType = enum 0

        override __.Namespace = null //notRequired "Namespace"
        override __.DeclaringType = notRequired "DeclaringType"
        override __.BaseType = notRequired "BaseType"
        override __.GetInterfaces() = notRequired "GetInterfaces"

        override this.GetConstructors(_bindingFlags) = notRequired "GetConstructors"
        override this.GetMethods(_bindingFlags) = notRequired "GetMethods"
        override this.GetField(_name, _bindingFlags) = notRequired "GetField"
        override this.GetFields(_bindingFlags) = notRequired "GetFields"
        override this.GetEvent(_name, _bindingFlags) = notRequired "GetEvent"
        override this.GetEvents(_bindingFlags) = notRequired "GetEvents"
        override this.GetProperties(_bindingFlags) = notRequired "GetProperties"
        override this.GetMembers(_bindingFlags) = notRequired "GetMembers"
        override this.GetNestedTypes(_bindingFlags) = notRequired "GetNestedTypes"
        override this.GetNestedType(_name, _bindingFlags) = notRequired "GetNestedType"
        override this.GetPropertyImpl(_name, _bindingFlags, _binder, _returnType, _types, _modifiers) = notRequired "GetPropertyImpl"
        override this.MakeGenericType(_args) = notRequired "MakeGenericType"
        override this.MakeArrayType() = ReflectTypeSymbol(ReflectTypeSymbolKind.SDArray, [| this |]) :> Type
        override this.MakeArrayType arg = ReflectTypeSymbol(ReflectTypeSymbolKind.Array arg, [| this |]) :> Type
        override this.MakePointerType() = ReflectTypeSymbol(ReflectTypeSymbolKind.Pointer, [| this |]) :> Type
        override this.MakeByRefType() = ReflectTypeSymbol(ReflectTypeSymbolKind.ByRef, [| this |]) :> Type

        override __.GetAttributeFlagsImpl() = TypeAttributes.Public ||| TypeAttributes.Class ||| TypeAttributes.Sealed 

        override __.IsArrayImpl() = false
        override __.IsByRefImpl() = false
        override __.IsPointerImpl() = false
        override __.IsPrimitiveImpl() = false
        override __.IsCOMObjectImpl() = false
        override __.IsGenericType = false
        override __.IsGenericTypeDefinition = false

        override __.HasElementTypeImpl() = false

        override this.UnderlyingSystemType = this :> Type
        override __.GetCustomAttributesData() = ReflectCustomAttribute.TxCustomAttributesData(asm, inp.Attribs)

        override __.ToString() = sprintf "ctxt generic param %s" inp.Name 

        override this.AssemblyQualifiedName                                                            = this.FullName + ", " + this.Assembly.FullName

        override __.GetGenericArguments() = notRequired "GetGenericArguments"
        override __.GetGenericTypeDefinition() = notRequired "GetGenericTypeDefinition"
        override __.GetMember(_name,_mt,_bindingFlags)                                                      = notRequired "TxILGenericParam: GetMember"
        override __.GUID                                                                                      = notRequired "TxILGenericParam: GUID"
        override __.GetMethodImpl(_name, _bindingFlags, _binder, _callConvention, _types, _modifiers)          = notRequired "TxILGenericParam: GetMethodImpl"
        override __.GetConstructorImpl(_bindingFlags, _binder, _callConvention, _types, _modifiers)           = notRequired "TxILGenericParam: GetConstructorImpl"
        override __.GetCustomAttributes(_inherited)                                                            = notRequired "TxILGenericParam: GetCustomAttributes"
        override __.GetCustomAttributes(_attributeType, _inherited)                                             = notRequired "TxILGenericParam: GetCustomAttributes"
        override __.IsDefined(_attributeType, _inherited)                                                       = notRequired "TxILGenericParam: IsDefined"
        override __.GetInterface(_name, _ignoreCase)                                                            = notRequired "TxILGenericParam: GetInterface"
        override __.Module                                                                                    = notRequired "TxILGenericParam: Module" : Module 
        override __.GetElementType()                                                                          = notRequired "TxILGenericParam: GetElementType"
        override __.InvokeMember(_name, _invokeAttr, _binder, _target, _args, _modifiers, _culture, _namedParameters) = notRequired "TxILGenericParam: InvokeMember"

    and ReflectTypar(asm: ReflectAssembly, _tp : Typar) =
        inherit Type()
        member __.Metadata = _tp
        override __.InvokeMember(_name, _invokeAttr, _binder, _target, _args, _modifiers, _culture, _namedParameters) = failwith ""
        override __.GetMembers(_) = failwith ""  
        override __.Assembly= asm :> Assembly
        override this.AssemblyQualifiedName= this.FullName + ", " + asm.FullName
        override __.BaseType= failwith "" 
        override __.FullName =_tp.Name
        override __.GetAttributeFlagsImpl()= failwith "" 
        override __.GetConstructorImpl(_,_,_,_,_)= failwith "" 
        override __.GetConstructors(_b)= failwith "" 
        override __.GetElementType()= failwith "" 
        override __.GetEvent(_s, _b) = failwith "" 
        override __.GetEvents(_b)= failwith "" 
        override __.GetField(_s, _b) = failwith "" 
        override __.GetFields(_b)= failwith "" 
        override __.GetInterface(_s, _b) = failwith "" 
        override __.GetInterfaces()= failwith "" 
        override __.GetMethodImpl(_,_,_,_,_,_)= failwith "" 
        override __.GetMethods(_b)= failwith "" 
        override __.GetNestedType(_s, _b) = failwith "" 
        override __.GetNestedTypes(_b)= failwith "" 
        override __.GetProperties(_b)= failwith "" 
        override __.GetPropertyImpl (_,_,_,_,_,_)= failwith "" 
        override __.GUID= failwith "" 
        override __.HasElementTypeImpl() =false
        override __.IsArrayImpl() =false
        override __.IsByRefImpl() =false
        override __.IsCOMObjectImpl() =false
        override __.IsPointerImpl() = false
        override __.IsPrimitiveImpl() =false
        override __.Module : Module= failwith "" 
        override __.Namespace=null
        override __.TypeHandle= failwith "" 
        override t.UnderlyingSystemType=t :> Type 
        override __.GetCustomAttributes(_b)= failwith "" 
        override __.GetCustomAttributes(_,_)= failwith "" 
        override __.IsDefined(_,_)= failwith ""
        override __.IsGenericParameter = true
        override __.Name=_tp.Name

    and ReflectConst =
        static member TxConst (cnst:Const) = 
            match cnst with 
            | Const.Bool b -> box b
            | Const.Byte b -> box b
            | Const.Char c -> box c
            | Const.Decimal d -> box d
            | Const.Double d -> box d
            | Const.Int16 i -> box i
            | Const.Int32 i -> box i
            | Const.Int64 i -> box i
            | Const.IntPtr i -> box i
            | Const.SByte sb -> box sb
            | Const.Single f -> box f
            | Const.String s -> box s
            | Const.UInt16 i -> box i
            | Const.UInt32 i -> box i
            | Const.UInt64 i -> box i
            | Const.UIntPtr i -> box i
            | Const.Unit -> box ()
            | Const.Zero -> box 0

    and ReflectCustomAttribute(asm : ReflectAssembly, tyconRef : TyconRef, exprs) = 
        inherit CustomAttributeData()
        let TxCustomAttributesArg (AttribExpr(_,v)) =
            match v with
            | Expr.Const (cnst, _, ttype) -> 
            //TODO: This can probably be removed completly.
                CustomAttributeTypedArgument(asm.TxTType ttype, ReflectConst.TxConst cnst)
            | _ -> failwithf "Missing case for CustomAttributesArg %+A" v
        override __.Constructor =  
            let constr = 
                tyconRef.MembersOfFSharpTyconSorted 
                |> List.find (fun x -> x.IsConstructor)
            ReflectConstructorDefinition (asm, asm.TxTypeDef None tyconRef, constr) :> ConstructorInfo
        override __.ConstructorArguments = [| for exp in exprs -> TxCustomAttributesArg exp |] :> IList<_>
        // Note, named arguments of custom attributes are not required by F# compiler on binding context elements.
        override __.NamedArguments = [| |] :> IList<_> 
        static member TxCustomAttributesData (asm : ReflectAssembly, attribs:Attribs) = //notRequired "custom attributes are not available for context assemblies"
             [| for Attrib(tcref, _, exprs, _, _, _, _) in attribs do 
                  yield ReflectCustomAttribute(asm, tcref, exprs) :> CustomAttributeData |]
             :> IList<CustomAttributeData>

    and
        [<AllowNullLiteral>]
        ReflectConstructorDefinition(asm : ReflectAssembly, declTy: Type, inp: ValRef) = 
            inherit ConstructorInfo()
                override __.Name = ".ctor"
                override __.Attributes = notRequired "Attributes" //TODO: Constructor attributes
                override __.MemberType        = MemberTypes.Constructor
                override __.DeclaringType = declTy

                override __.GetParameters() = notRequired ".ctor parameters" //TODO: Constructor parameters
                override __.GetCustomAttributesData() = ReflectCustomAttribute.TxCustomAttributesData (asm, inp.Attribs)

                override __.GetHashCode() = 0
                override __.Equals(that:obj) = 
                    match that with 
                    | :? ConstructorInfo as that -> 
                        eqType declTy that.DeclaringType //&&
                        //TODO: Equality on constructor parameters
                        //eqParametersAndILParameterTypesWithInst gps (that.GetParameters()) inp.Parameters 
                    | _ -> false

                override __.IsDefined(_attributeType, _inherited) = notRequired "IsDefined" 
                override __.Invoke(_invokeAttr, _binder, _parameters, _culture) = notRequired "Invoke"
                override __.Invoke(_obj, _invokeAttr, _binder, _parameters, _culture) = notRequired "Invoke"
                override __.ReflectedType = notRequired "ReflectedType"
                override __.GetMethodImplementationFlags() = notRequired "GetMethodImplementationFlags"
                override __.MethodHandle = notRequired "MethodHandle"
                override __.GetCustomAttributes(_inherited) = notRequired "GetCustomAttributes"
                override __.GetCustomAttributes(_attributeType, _inherited) = notRequired "GetCustomAttributes"

                override __.ToString() = sprintf "ctxt constructor(...) in type %s" declTy.FullName

    /// Clones namespaces, type providers, types and members provided by tp, renaming namespace nsp1 into namespace nsp2.
    and
        [<AllowNullLiteral>]
        ReflectMethodDefinition(declTy: Type, vref: ValRef, asm : ReflectAssembly) =
        inherit MethodInfo()
            //TODO: Handle generic parameters
        let _mi = match vref.ValReprInfo with
                    | None -> failwith ""
                    | Some _mi -> _mi

        let gps = if declTy.IsGenericType then declTy.GetGenericArguments() else [| |]
        let rec gps2 = 
            match vref.Type with
            | TType_forall (pars,_) -> 
                pars |> List.mapi (fun i gp -> ReflectGenericParam(asm, i + gps.Length, gp) :> Type) |> List.toArray
            | _ -> [||]


        let TxParameter (t : TType) : ParameterInfo =
            { new ParameterInfo() with
                member __.ParameterType = asm.TxTType t
            }
        
        let TxMethodAttribute (_vref: ValRef) : MethodAttributes =
            //TODO: Implement
            MethodAttributes.Static

        let argTys, retTy = 
            let rec go = function
            | TType_fun(args,r) ->
                let argTys =
                    match args with
                    | TType_tuple(_,ts) -> ts |> Array.ofList
                    | _ -> [|args|]
                argTys, r
            | TType_var(v) when v.IsSolved -> go v.Solution.Value
            | _ -> failwith "Unreachable"

            go vref.Type

        member __.Metadata = vref

        override __.Name              = vref.CompiledName  
        override __.DeclaringType     = declTy
        override __.MemberType        = MemberTypes.Method
        override __.Attributes        =
            TxMethodAttribute vref
        override __.GetParameters()   =
            argTys |> Array.map TxParameter
        override __.CallingConvention = CallingConventions.HasThis ||| CallingConventions.Standard // Provided types report this by default
        override __.ReturnType        = retTy |> asm.TxTType
        override __.GetCustomAttributesData() = ReflectCustomAttribute.TxCustomAttributesData(asm, vref.Attribs)
        override __.GetGenericArguments() = gps2
        override __.IsGenericMethod = (gps2.Length <> 0)
        override __.IsGenericMethodDefinition = __.IsGenericMethod

        override __.GetHashCode() = hash vref.Stamp //TODO: Implement correct hashing  + hashILParameterTypes inp.Parameters
        override this.Equals(that:obj) = 
            match that with 
            | :? MethodInfo as thatMI -> 
                vref.CompiledName = thatMI.Name 
                (*
                TODO: Need to implement equality correctly for method defs
                &&
                eqType this.DeclaringType thatMI.DeclaringType &&
                eqParametersAndILParameterTypesWithInst gps (thatMI.GetParameters()) inp.Parameters *)
            | _ -> false

        override this.MakeGenericMethod(args) = ReflectMethodSymbol(this, args) :> MethodInfo

        override __.MetadataToken = int vref.Stamp //TODO: Fix me .MetadataToken

        // unused
        override __.MethodHandle = notRequired "MethodHandle"
        override __.ReturnParameter = notRequired "ReturnParameter" 
        override __.IsDefined(_attributeType, _inherited) = notRequired "IsDefined"
        override __.ReturnTypeCustomAttributes = notRequired "ReturnTypeCustomAttributes"
        override __.GetBaseDefinition() = notRequired "GetBaseDefinition"
        override __.GetMethodImplementationFlags() = notRequired "GetMethodImplementationFlags"
        override __.Invoke(_obj, _invokeAttr, _binder, _parameters, _culture)  = notRequired "Invoke"
        override __.ReflectedType = notRequired "ReflectedType"
        override __.GetCustomAttributes(_inherited) = notRequired "GetCustomAttributes"
        override __.GetCustomAttributes(_attributeType, _inherited) = notRequired "GetCustomAttributes" 

        override __.ToString() = sprintf "ctxt method %s(...) in type %s" vref.CompiledName declTy.FullName

    /// Makes a type definition read from a binary available as a System.Type. Not all methods are implemented.
    and ReflectTypeDefinition (asm: ReflectAssembly, declTyOpt: Type option, tcref: TyconRef) as this = 
        inherit Type()

        // Note: For F# type providers we never need to view the custom attributes
        let rec TxMethodDef (declTy: Type) (vref: ValRef) =
            ReflectMethodDefinition(declTy, vref, asm) :> MethodInfo

        /// Makes a parameter definition read from a binary available as a ParameterInfo. Not all methods are implemented.
        //let rec TxILParameter gps (inp : TyconRef) = 
        //    { new ParameterInfo() with 

        //        override __.Name = inp.MembersOfFSharpTyconByName.["Foo"].[0].MemberInfo.Value.
        //        override __.ParameterType = inp.ParameterType |> TxILType gps
        //        override __.RawDefaultValue = (match inp.Default with None -> null | Some v -> convFieldInit v)
        //        override __.Attributes = inp.Attributes
        //        override __.GetCustomAttributesData() = inp.CustomAttrs  |> TxCustomAttributesData

        //        override x.ToString() = sprintf "ctxt parameter %s" x.Name }

        /// Makes a property definition read from a binary available as a PropertyInfo. Not all methods are implemented.
        and TxPropertyDefinition declTy _ (* gps *) (tycon:TyconRef) (inp: ValRef) = 
            { new PropertyInfo() with 

                override __.Name = inp.PropertyName
                override __.Attributes        = notRequired "TxPropertyDefinition Attributes" //TODO: TxPropertyDefinition method attributes
                override __.MemberType = MemberTypes.Property
                override __.DeclaringType = declTy

                override __.PropertyType = inp.Type |> asm.TxTType
                override __.GetGetMethod(_nonPublic) = 
                    tycon.MembersOfFSharpTyconByName.[inp.PropertyName] 
                    |> List.tryFind (fun x -> x.IsPropertyGetterMethod) 
                    |> Option.map (TxMethodDef declTy)
                    |> optionToNull
                override __.GetSetMethod(_nonPublic) =
                    tycon.MembersOfFSharpTyconByName.[inp.PropertyName] 
                    |> List.tryFind (fun x -> x.IsPropertySetterMethod) 
                    |> Option.map (TxMethodDef declTy)
                    |> optionToNull
                override __.GetIndexParameters() = 
                    //TODO: Implement Property index parameters
                    notRequired "TxPropertyDefinition : GetIndexParameters"
                    //inp.IndexParameters |> Array.map (TxILParameter (gps, [| |]))
                override __.CanRead =
                    tycon.MembersOfFSharpTyconByName.[inp.PropertyName] 
                    |> List.tryFind (fun x -> x.IsPropertyGetterMethod)
                    |> fun x -> x.IsSome
                override __.CanWrite =
                    tycon.MembersOfFSharpTyconByName.[inp.PropertyName] 
                    |> List.tryFind (fun x -> x.IsPropertySetterMethod)
                    |> fun x -> x.IsSome
                override __.GetCustomAttributesData() = ReflectCustomAttribute.TxCustomAttributesData(asm, inp.Attribs)

                override this.GetHashCode() = hash inp.CompiledName
                override this.Equals(that:obj) = 
                    match that with 
                    | :? PropertyInfo as thatPI -> 
                        inp.CompiledName = thatPI.Name  &&
                        eqType this.DeclaringType thatPI.DeclaringType 
                    | _ -> false

                override __.GetValue(obj, invokeAttr, binder, index, culture) = notRequired "GetValue"
                override __.SetValue(obj, _value, invokeAttr, binder, index, culture) = notRequired "SetValue"
                override __.GetAccessors(nonPublic) = notRequired "GetAccessors"
                override __.ReflectedType = notRequired "ReflectedType"
                override __.GetCustomAttributes(inherited) = notRequired "GetCustomAttributes"
                override __.GetCustomAttributes(attributeType, inherited) = notRequired "GetCustomAttributes"
                override __.IsDefined(attributeType, inherited) = notRequired "IsDefined"

                override __.ToString() = sprintf "ctxt property %s(...) in type %s" inp.CompiledName declTy.Name }

        /// Make an event definition read from a binary available as an EventInfo. Not all methods are implemented.
        //and TxEventDefinition declTy gps (inp: ILEventDef) = 
        //    { new EventInfo() with 

        //        override __.Name = inp.Name 
        //        override __.Attributes = inp.Attributes
        //        override __.MemberType = MemberTypes.Event
        //        override __.DeclaringType = declTy

        //        override __.EventHandlerType = inp.EventHandlerType |> TxILType (gps, [| |])
        //        override __.GetAddMethod(_nonPublic) = inp.AddMethod |> TxILMethodRef
        //        override __.GetRemoveMethod(_nonPublic) = inp.RemoveMethod |> TxILMethodRef
        //        override __.GetCustomAttributesData() = inp.CustomAttrs |> TxCustomAttributesData

        //        override __.GetHashCode() = hash inp.Name
        //        override this.Equals(that:obj) = 
        //            match that with 
        //            | :? EventInfo as thatEI -> 
        //                inp.Name = thatEI.Name  &&
        //                eqType this.DeclaringType thatEI.DeclaringType 
        //            | _ -> false

        //        override __.GetRaiseMethod(nonPublic) = notRequired "GetRaiseMethod"
        //        override __.ReflectedType = notRequired "ReflectedType"
        //        override __.GetCustomAttributes(inherited) = notRequired "GetCustomAttributes"
        //        override __.GetCustomAttributes(attributeType, inherited)  = notRequired "GetCustomAttributes"
        //        override __.IsDefined(attributeType, inherited) = notRequired "IsDefined"

        //        override __.ToString() = sprintf "ctxt event %s(...) in type %s" inp.Name declTy.FullName }

        and TxFieldDefinition declTy _ (* gps *) (inp: RecdField) = 
            { new FieldInfo() with 

                override __.Name = inp.Name 
                override __.Attributes = 
                    [|
                        yield if inp.rfield_static then Some FieldAttributes.Static else None
                        yield if inp.rfield_const.IsSome then Some FieldAttributes.Literal else None
                    |] 
                    |> Array.choose id
                    |> Array.fold (|||) (if inp.rfield_secret then FieldAttributes.Public else FieldAttributes.Private)
                override __.MemberType = MemberTypes.Field 
                override __.DeclaringType = declTy
                override __.FieldType = inp.FormalType |> asm.TxTType
                override __.GetRawConstantValue()  = match inp.LiteralValue with None -> null | Some v -> ReflectConst.TxConst v
                override __.GetCustomAttributesData() = [| |] :> IList<_> // notRequired "CustomAttribute data" // inp.FieldAttribs |> TxCustomAttributesData

                override __.GetHashCode() = hash inp.Name
                override this.Equals(that:obj) = 
                    match that with 
                    | :? EventInfo as thatFI -> 
                        inp.Name = thatFI.Name  &&
                        eqType this.DeclaringType thatFI.DeclaringType 
                    | _ -> false
        
                override __.ReflectedType = notRequired "ReflectedType"
                override __.GetCustomAttributes(inherited) = notRequired "GetCustomAttributes"
                override __.GetCustomAttributes(attributeType, inherited) = notRequired "GetCustomAttributes"
                override __.IsDefined(attributeType, inherited) = notRequired "IsDefined"
                override __.SetValue(obj, _value, invokeAttr, binder, culture) = notRequired "SetValue"
                override __.GetValue(obj) = notRequired "GetValue"
                override __.FieldHandle = notRequired "FieldHandle"

                override __.ToString() = sprintf "ctxt literal field %s(...) in type %s" inp.Name declTy.FullName }

        ///// Bind a reference to a constructor
        and TxConstructor (mref: ValRef) = 
            let argTypes = [||]//Array.map (TxILType ([| |], [| |])) mref.  
            let declTy = asm.TxTType mref.Type
            let cons = declTy.GetConstructor(BindingFlags.Public ||| BindingFlags.NonPublic, null, argTypes, null)
            if cons = null then failwith (sprintf "constructor reference '%+A' not resolved" mref)
            cons

        let rec gps = tcref.TyparsNoRange |> List.mapi (fun i gp -> ReflectGenericParam (asm, i, gp) :> Type) |> List.toArray

        member this.isNested =
            this.FullName.Contains("+")

        override __.Name = tcref.CompiledName 
        override __.Assembly = (asm :> Assembly) 
        override __.DeclaringType = declTyOpt |> optionToNull
        override this.MemberType =
            if this.isNested then MemberTypes.NestedType else MemberTypes.TypeInfo

        override this.AssemblyQualifiedName =
            this.FullName + ", " + this.Assembly.FullName

        override __.FullName = 
            tcref.CompiledRepresentationForNamedType.FullName
                        
        override __.Namespace =
            let enclosing = tcref.CompiledRepresentationForNamedType.Enclosing
            if enclosing.Length > 0 then
                let outerType = enclosing.[0]
                if outerType.Contains(".") then
                    let i = outerType.LastIndexOf('.')
                    outerType.[0..i-1]
                else
                    null
            else
                null

        override __.BaseType = null//inp. |> Option.map (TxILType (gps, [| |])) |> optionToNull
        override __.GetInterfaces() = tcref.ImmediateInterfaceTypesOfFSharpTycon |> List.map asm.TxTType |> List.toArray

        override this.GetConstructors(_bindingFlags) = 
            tcref.MembersOfFSharpTyconSorted 
            |> List.filter (fun x -> x.IsConstructor)
            |> List.map (TxConstructor)
            |> List.toArray

        override this.GetMethods(_bindingFlags) = 
            tcref.Deref.entity_tycon_tcaug.tcaug_adhoc_list 
            |> Seq.map (snd >> TxMethodDef this)
            |> Seq.toArray

        override this.GetField(name, _bindingFlags) = 
            tcref.AllFieldTable.FieldByName(name)
            |> Option.map (TxFieldDefinition this gps) 
            |> optionToNull

        override this.GetFields(_bindingFlags) = 
            tcref.AllFieldsArray
            |> Array.map (TxFieldDefinition this gps)

        override this.GetEvent(name, _bindingFlags) = 
            //TODO: Need to implement events
            notRequired (sprintf "Could not get event %+A" name) 
            //inp.Events.Elements 
            //|> Array.tryPick (fun ev -> if ev.Name = name then Some (TxEventDefinition this gps ev) else None) 
            //|> optionToNull

        override this.GetEvents(_bindingFlags) = 
            //TODO: Need to implement events
            notRequired "Could not get events"

        override this.GetProperties(_bindingFlags) = 
            let isProperty (kind:Ast.MemberKind option) =
                match kind with
                | Some kind ->
                    kind = Ast.MemberKind.PropertyGet
                    || kind = Ast.MemberKind.PropertyGetSet
                    || kind = Ast.MemberKind.PropertySet
                | None -> false

            tcref.MembersOfFSharpTyconSorted
            |> List.filter (fun x -> x.MemberInfo |> Option.map (fun x -> x.MemberFlags.MemberKind) |> isProperty)
            |> List.map (TxPropertyDefinition this gps tcref)
            |> List.toArray

        override this.GetMembers(_bindingFlags) = 
            [| for x in this.GetMethods() do yield (x :> MemberInfo)
               for x in this.GetFields() do yield (x :> MemberInfo)
               for x in this.GetProperties() do yield (x :> MemberInfo)
               for x in this.GetEvents() do yield (x :> MemberInfo)
               for x in this.GetNestedTypes() do yield (x :> MemberInfo) |]
     
        override this.GetNestedTypes(_bindingFlags) = 
            [| match tcref.TypeReprInfo with 
               | TProvidedTypeExtensionPoint info ->
                    ignore info
                   //for nestedType in info.ProvidedType.PApplyArray((fun sty -> sty.GetNestedTypes()), "GetNestedTypes", range0) do 
                   //   let nestedTypeName = nestedType.PUntaint((fun t -> t.Name), range0)
                   
                   //   let nestedTcref = mkNonLocalTyconRef tcref.nlr nestedTypeName
                   //   let nestedTcref = LookupTypeNameInEntityMaybeHaveArity (ncenv.amap, m, ad, nestedTypeName, staticResInfo, tcref) 
                   
                   //   for nestedTcref in nestedTcref do
                   //      yield  asm.TxTypeDef (Some (this :> Type)) nestedTcref
                    
               | _ -> 
                   for entity in tcref.ModuleOrNamespaceType.TypesByAccessNames.Values do
                       yield asm.TxTypeDef (Some (this :> Type))  (tcref.NestedTyconRef entity)
             |]
     
        // GetNestedType is used for linking to the binding context
        override this.GetNestedType(name, _bindingFlags) = 
            match tcref.TypeReprInfo with 
            | TProvidedTypeExtensionPoint info ->
                ignore info
                // TODO: nested types in provided types
                null
                    
               | _ -> 
                   match tcref.ModuleOrNamespaceType.TypesByMangledName.TryFind name with 
                   | None -> null
                   | Some entity -> asm.TxTypeDef (Some (this :> Type))  (tcref.NestedTyconRef entity) 

        override this.GetPropertyImpl(name, _bindingFlags, _binder, _returnType, expectedTypes, _modifiers) = 
            let matches (p : PropertyInfo) =
                let types = p.GetIndexParameters() |> Array.map (fun p -> p.ParameterType)
                p.Name = name && types = expectedTypes
            this.GetProperties()
            |> Array.tryFind matches
            |> optionToNull
            
        override this.GetMethodImpl( name, _bindingFlags, _binder, _callConvention, expectedTypes, _modifiers) =
            let matches (p : MethodInfo) =
                let types = p.GetParameters() |> Array.map (fun p -> p.ParameterType)
                p.Name = name && types = expectedTypes
            this.GetMethods()
            |> Array.tryFind matches
            |> optionToNull

        override this.GetConstructorImpl(_bindingFlags, _binder, _callConvention, expectedTypes, _modifiers) = 
            let matches (p : ConstructorInfo) =
                let types = p.GetParameters() |> Array.map (fun p -> p.ParameterType)
                types = expectedTypes
            this.GetConstructors()
            |> Array.tryFind matches
            |> optionToNull

        // Every implementation of System.Type must meaningfully implement these
        override this.MakeGenericType(args) = ReflectTypeSymbol(ReflectTypeSymbolKind.Generic this, args) :> Type
        override this.MakeArrayType() = ReflectTypeSymbol(ReflectTypeSymbolKind.SDArray, [| this |]) :> Type
        override this.MakeArrayType arg = ReflectTypeSymbol(ReflectTypeSymbolKind.Array arg, [| this |]) :> Type
        override this.MakePointerType() = ReflectTypeSymbol(ReflectTypeSymbolKind.Pointer, [| this |]) :> Type
        override this.MakeByRefType() = ReflectTypeSymbol(ReflectTypeSymbolKind.ByRef, [| this |]) :> Type

        member this.AttributeFlags =
            let pickAttr =
                List.tryFind fst
                >> Option.map snd
                >> Option.defaultValue (TypeAttributes())

            let visibility =
                let isNotPublic = false
                let isPublic = false
                let isNestedPublic = true
                let isNestedPrivate = false
                let isNestedFamily = false
                let isNestedAssembly = false
                let isNestedFamANDAssem = false
                let isNestedFamORAssem = false
                [
                    isNotPublic, TypeAttributes.NotPublic
                    isPublic, TypeAttributes.Public
                    isNestedPublic, TypeAttributes.NestedPublic
                    isNestedPrivate, TypeAttributes.NestedPrivate
                    isNestedFamily, TypeAttributes.NestedFamily
                    isNestedAssembly, TypeAttributes.NestedAssembly
                    isNestedFamANDAssem, TypeAttributes.NestedFamANDAssem
                    isNestedFamORAssem, TypeAttributes.NestedFamORAssem
                ] |> pickAttr
            let classSemantics =
                let isInterface = isInterfaceTyconRef tcref
                let isClass = not isInterface
                [
                    isInterface, TypeAttributes.Interface
                    isClass, TypeAttributes.Class
                ] |> pickAttr

            let layout =
                let isAutoLayout = true
                let isSequentialLayout = false
                let isExplicitLayout = false
                [
                    isAutoLayout, TypeAttributes.AutoLayout
                    isSequentialLayout, TypeAttributes.SequentialLayout
                    isExplicitLayout, TypeAttributes.ExplicitLayout
                ] |> pickAttr
            let stringFormat =
                let isAnsi = true
                let isUnicode = false
                let isAuto = false
                let isCustom = false
                [
                    isAnsi, TypeAttributes.AnsiClass
                    isUnicode, TypeAttributes.UnicodeClass
                    isAuto, TypeAttributes.AutoClass
                    isCustom, TypeAttributes.CustomFormatClass
                ] |> pickAttr
            let isAbstract = isAbstractTycon (tcref.Deref)
            let isBeforeFieldInit = false
            let hasSecurity = false
            let import = false
            let hasRTSpecialName = false
            let isSealed = true
            let isSerializable = true
            let windowsRuntime = true
            [
                isAbstract, TypeAttributes.Abstract
                isBeforeFieldInit, TypeAttributes.BeforeFieldInit
                hasSecurity, TypeAttributes.HasSecurity
                import, TypeAttributes.Import
                hasRTSpecialName, TypeAttributes.RTSpecialName
                isSealed, TypeAttributes.Sealed
                isSerializable, TypeAttributes.Serializable
                windowsRuntime, TypeAttributes.WindowsRuntime
                true, unbox (int TypeProviderTypeAttributes.IsErased)
            ]
            |> List.filter fst
            |> List.map snd
            |> List.fold (|||) (TypeAttributes())
            ||| visibility
            ||| classSemantics
            ||| layout
            ||| stringFormat

        override this.GetAttributeFlagsImpl() = this.AttributeFlags
            

        override __.IsValueTypeImpl() = tcref.IsStructOrEnumTycon || tcref.IsFSharpStructOrEnumTycon
        override __.IsArrayImpl() = false
        override __.IsByRefImpl() = false
        override __.IsPointerImpl() = false
        override __.IsPrimitiveImpl() = false
        override __.IsCOMObjectImpl() = false
        override __.IsGenericType = (gps.Length <> 0)
        override __.IsGenericTypeDefinition = (gps.Length <> 0)
        override __.HasElementTypeImpl() = false

        override this.UnderlyingSystemType = (this :> Type)
        override this.GetCustomAttributesData() =
            let missingAttribs = [this.CompilationMappingAttribute; this.SerializableAttribute] |> List.choose id
            [| yield! (ReflectCustomAttribute.TxCustomAttributesData(asm, tcref.Attribs))
               yield! missingAttribs |] :> IList<_>

        override this.Equals(that:obj) =
            match that with
            | :? ReflectTypeDefinition as that -> this.Metadata.Stamp = that.Metadata.Stamp
            | _ -> false
        override this.GetHashCode() =  hash tcref.CompiledName
        override this.IsAssignableFrom(otherTy) = base.IsAssignableFrom(otherTy) || this.Equals(otherTy)
        override this.IsSubclassOf(otherTy) = base.IsSubclassOf(otherTy) || tcref.IsFSharpDelegateTycon && otherTy = typeof<Delegate> // F# quotations implementation

        override this.ToString() = this.FullName
        
        override __.GetGenericArguments() = gps
        override __.GetGenericTypeDefinition() = this :> Type //notRequired "GetGenericTypeDefinition"
        override __.GetMember(_name, _memberType, _bindingFlags)                                                      = notRequired "TxILTypeDef: GetMember"
        override __.GUID                                                                                      = notRequired "TxILTypeDef: GUID"
        override __.GetCustomAttributes(_inherited)                                                            = notRequired "TxILTypeDef: GetCustomAttributes"
        override __.GetCustomAttributes(_attributeType, _inherited)                                             = notRequired "TxILTypeDef: GetCustomAttributes"
        override __.IsDefined(_attributeType, _inherited)                                                       = notRequired "TxILTypeDef: IsDefined"
        override __.GetInterface(_name, _ignoreCase)                                                            = notRequired "TxILTypeDef: GetInterface"
        override __.Module                                                                                    = notRequired "TxILTypeDef: Module" : Module 
        override __.GetElementType()                                                                          = notRequired "TxILTypeDef: GetElementType"
        override __.InvokeMember(_name, _invokeAttr, _binder, _target, _args, _modifiers, _culture, _namedParameters) = notRequired "TxILTypeDef: InvokeMember"

        member x.Metadata = tcref
        member x.MakeMethodInfo (declTy,md) = TxMethodDef declTy md
        member x.MakeConstructorInfo (declTy,md) = ReflectConstructorDefinition(asm, declTy, md)

        member x.isSerializable = true //TODO: Implement

        member x.SerializableAttribute : CustomAttributeData option =
            if x.isSerializable then
                Some <| 
                    { new CustomAttributeData () with
                        member __.Constructor = typeof<SerializableAttribute>.GetConstructors().[0]
                        member __.ConstructorArguments = [| |] :> _
                        member __.NamedArguments = [| |] :> _
                    }
            else
                None

        member x.CompilationMappingAttribute : CustomAttributeData option =
            let flags =
                if tcref.IsRecordTycon then
                    Some SourceConstructFlags.RecordType
                elif tcref.IsUnionTycon then
                    Some SourceConstructFlags.SumType
                elif tcref.IsExceptionDecl then
                    Some SourceConstructFlags.Exception
                elif tcref.IsFSharpObjectModelTycon then
                    Some SourceConstructFlags.ObjectType
                elif tcref.IsFSharpStructOrEnumTycon then
                    Some SourceConstructFlags.Value
                else
                    None
            flags |> Option.map (fun f ->
                 { new CustomAttributeData () with
                    member __.Constructor = typeof<CompilationMappingAttribute>.GetConstructors().[0]
                    member __.ConstructorArguments = [| new CustomAttributeTypedArgument(f) |] :> _
                    member __.NamedArguments = [| |] :> _
                 }
                )

    and ReflectAssembly(g, ccu: CcuThunk, location:string) as asm =
        inherit Assembly()

        // A table tracking how type definition objects are translated.
        let txTable = TxTable<Type>() // CHANGE THIS TO CONTAIN TYPES, DO CASTS WHEN NEEDED
        let txTypeDef (declTyOpt: Type option) (inp: TyconRef) =
            txTable.Get inp.Stamp (fun () -> ReflectTypeDefinition(asm, declTyOpt, inp) :> Type)
        let txTypeVar (inp: Typar) =
            txTable.Get inp.Stamp (fun () -> ReflectTypar(asm, inp) :> Type)

        let name =
            lazy
            new AssemblyName(
                match ccu.ILScopeRef with
                | ILScopeRef.Local -> ccu.AssemblyName
                | _ -> ccu.ILScopeRef.QualifiedNameWithNoShortPrimaryAssembly
            )
        let fullName =
            let printWithDefault x d = if x |> isNull then d else x.ToString()
            lazy
                let name = name.Value
                if name.Version |> isNull then
                    sprintf "%s, Version=%s, Culture=%s, PublicKeyToken=%s" 
                        name.Name
                        (printWithDefault name.Version "0.0.0.0") 
                        (printWithDefault name.CultureInfo "neutral")
                        (printWithDefault (name.GetPublicKeyToken()) "null")
                else
                    name.FullName
        let types = lazy [| for td in ccu.RootModulesAndNamespaces -> txTypeDef None (mkLocalEntityRef td) |]

        override x.GetTypes () = types.Value
        override x.Location = location

        override x.GetType (path:string) =
            let matches name (t : Type) =
                name = t.FullName
            let findType name =
                txTable.Values
                |> Seq.tryFind (matches name)
            if path.Contains("[") then
                let argI, argE = path.IndexOf("["), path.LastIndexOf("]")
                let path2, args = path.[0..argI-1], path.[argI+1..argE-1]
                let genTypeArgs = 
                    args.Split([|","|], StringSplitOptions.RemoveEmptyEntries) 
                    |> Array.map (fun a ->
                        match x.GetType(a) with
                        | null -> Type.GetType(a)
                        | a -> a
                    )
                match findType path2 with
                | Some t ->
                    if genTypeArgs.Length = 0 then
                        t
                    else
                        t.MakeGenericType(genTypeArgs)
                | None -> null
            else
                findType path
                |> Option.toObj

        override x.GetName () = name.Value

        override x.FullName = fullName.Value

        override x.ReflectionOnly = true

        override x.GetManifestResourceStream(_:string) = 
            notRequired "GetManifestResourceStreams"

        override x.ToString() = "ctxt assembly " + x.FullName


        member __.TxTypeDef declTyOpt inp = txTypeDef declTyOpt inp

            /// Makes a field definition read from a binary available as a FieldInfo. Not all methods are implemented.
        member asm.TxTType (typ:TType) = 
            // TODO: may need something special for "System.Void"
            let typ = stripTyEqnsWrtErasure Erasure.EraseAll g typ
            match typ with 
            | AppTy g (tcref, tinst) -> 
                let ccuofTyconRef = 
                    match ccuOfTyconRef tcref with 
                    | Some ccuofTyconRef -> ccuofTyconRef
                    | None -> 
                    match ccu.GetCcuBeingCompiledHack() with 
                    | Some ccuofTyconRef -> ccuofTyconRef
                    | None -> failwith (sprintf "TODO: didn't get back to CCU being compiled for local tcref %s" tcref.DisplayName)
                let reflAssem = ccuofTyconRef.ReflectAssembly :?> ReflectAssembly
                let tcrefR = reflAssem.TxTypeDef None tcref
                match tinst with 
                | [] -> tcrefR 
                | args -> tcrefR.MakeGenericType(Array.map asm.TxTType (Array.ofList args))  
            | ty when isArrayTy g ty -> 
                let ety = destArrayTy g ty 
                let etyR = asm.TxTType ety
                match rankOfArrayTy g ty with
                | 1 -> etyR.MakeArrayType()  
                | n -> etyR.MakeArrayType(n)  
            | ty when isNativePtrTy g ty -> 
                let etyR = destNativePtrTy g ty  |> asm.TxTType 
                etyR.MakePointerType()  
            | ty when isByrefTy g ty -> 
                let etyR = destByrefTy g ty  |> asm.TxTType 
                etyR.MakeByRefType()  
            | ty when isTyparTy g ty -> 
                let tp = destTyparTy g ty
                txTypeVar tp
            | _ -> failwithf "Unsupported TxTType %+A" typ
      
#endif