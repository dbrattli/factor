/// Cowboy HTTP server setup for the timeflies demo.
module FactorTimefliesApp

open Fable.Core
open Fable.Beam.Application
open Fable.Beam.Io
open Fable.Beam.Erlang
open Fable.Beam.Cowboy.Cowboy
open Fable.Beam.Cowboy.CowboyRouter

/// Cowboy route: {Path, Handler, Opts}
[<Emit("{$0, $1, []}")>]
let private route (path: string) (handler: obj) : obj = nativeOnly

/// Cowboy host rule: {'_', Routes}
[<Emit("{'_', $0}")>]
let private hostRule (routes: obj list) : obj = nativeOnly

/// Protocol options: #{env => #{dispatch => Dispatch}}
[<Emit("#{env => #{dispatch => $0}}")>]
let private protoOpts (dispatch: obj) : obj = nativeOnly

/// Transport options: [{port, Port}]
[<Emit("[{port, $0}]")>]
let private transportOpts (port: int) : obj = nativeOnly

let start () =
    application.ensure_all_started (binaryToAtom "cowboy") |> ignore

    let dispatch =
        compile [
            hostRule [
                route "/" (binaryToAtom "factor_timeflies_http")
                route "/ws" (binaryToAtom "factor_timeflies_ws")
            ]
        ]

    startClear (binaryToAtom "timeflies_listener") (transportOpts 3000) (protoOpts dispatch)
    |> ignore

    io.format ("Timeflies demo running at http://localhost:3000~n", [])
