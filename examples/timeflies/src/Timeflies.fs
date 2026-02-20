module Timeflies

open Factor.Types
open Factor.Rx
open Factor.Builder

let text = "TIME FLIES LIKE AN ARROW"

type MousePos = { X: int; Y: int }

type LetterPos = { Index: int; X: int; Y: int }

/// Sets up the reactive pipeline for the timeflies demo.
///
/// Takes a sendFn that sends a JSON string to the WebSocket client.
/// Returns (mouseObserver, disposable) where mouseObserver receives
/// mouse position events and disposable cleans up the pipeline.
let setupPipeline (sendFn: string -> unit) : Handler<MousePos, string> * Handle =
    let (mouseHandler, mouseMoves) = subject ()

    let lettersWithIndex =
        text
        |> Seq.toList
        |> List.mapi (fun i c -> (i, c))

    let stream =
        lettersWithIndex
        |> ofList
        |> flatMap (fun (index, _char) ->
            factor {
                let! (pos: MousePos) = mouseMoves |> delay (80 * index)

                return
                    { Index = index
                      X = pos.X + index * 14 + 15
                      Y = pos.Y }
            })

    let outputHandler: Handler<LetterPos, string> =
        { Notify =
            fun n ->
                match n with
                | OnNext lp ->
                    let json =
                        sprintf "{\"index\":%d,\"x\":%d,\"y\":%d}" lp.Index lp.X lp.Y

                    sendFn json
                | OnError _
                | OnCompleted -> () }

    let handle = subscribe outputHandler stream

    (mouseHandler, handle)
