#r "../../../../../../Debug/net40/bin/test_tp.dll"

open System
open FSharp.Reflection
open Microsoft.FSharp.Quotations
open Test

type OtherRecord =
  {
    x : bool
  }

type MyRecord =
  {
    x : int
    y : string
    z : OtherRecord
  }

type Lens<'a> = Testing.Lens<'a>

let mr = { x = 5; y = "hello"; z = { x = true } }

type OtherRecordL = Lens<OtherRecord>
type MyRecordL = Lens<MyRecord>

let inline (^.) x l = (fst l) x
let inline (%~) x ((get, set), f) = set x (f (get x))
let inline (>=>) (get2 : 'b -> 'c, set2: 'b -> 'c -> 'b) (get1 : 'a -> 'b, set1 : 'a -> 'b -> 'a) =
    let get x = get2 (get1 x)
    let set (b : 'a)  (s : 'c) = set1 b (set2 (get1 b) s)
    get, set

printfn "%A" (mr ^. (OtherRecordL.x >=> MyRecordL.z))


//type MyRecordArray = Testing.Arrayify<MyRecord>

//let mra = MyRecordArray 10
//mra.x.[5] <- 5
//mra.y.[3] <- "hello"
//printfn "%A, %A" mra.x mra.y

//[<Interface>]
//type X =
//  abstract member X : unit -> int
//
//type Y() =
//  interface X with
//    member __.X() = 5
//
//type Z() =
//  interface X with
//    member __.X() = 6
//
//let x : X = if true then Z() :> X else Y() :> X
//
//printfn "%A" (x.X())


//type MyInt1 =
//  static member X() : int = 5
//
//type MyInt2 =
//  static member X() : int = 6
//
//type MyAdd =
//  static member X() : int -> int -> int = fun a b -> a + b
//
//type Id<'a> = Testing.Id<'a>
//
//
//printfn "%A" <| (Id<MyAdd>.X()) (Id<MyInt1>.X()) (Id<MyInt2>.X())
