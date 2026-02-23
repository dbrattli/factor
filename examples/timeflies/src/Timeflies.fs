module Timeflies

open Factor.Types
open Factor.Reactive
open Factor.Flow

let text = "TIME FLIES LIKE AN ARROW WITH F# AND FABLE.BEAM"

type MousePos = { X: int; Y: int }

type LetterPos = { Index: int; Char: string; X: int; Y: int }

/// Sets up the reactive pipeline for the timeflies demo.
///
/// Takes a sendFn that sends a JSON string to the WebSocket client.
/// Returns (mouseSender, disposable) where mouseSender receives
/// mouse position events and disposable cleans up the pipeline.
let setupPipeline (sendFn: string -> unit) : Sender<MousePos> * Handle =
    let mouseSender, mouseMoves = channel ()

    let lettersWithIndex =
        text
        |> Seq.toList
        |> List.mapi (fun i c -> (i, c))

    let pipeline =
        lettersWithIndex
        |> ofList
        |> flatMap (fun (index, char) ->
            flow {
                let! (pos: MousePos) = mouseMoves |> delay (80 * index)

                return
                    { Index = index
                      Char = string char
                      X = pos.X + index * 14 + 15
                      Y = pos.Y }
            })

    let handle =
        pipeline
        |> subscribe
            (fun lp ->
                let json =
                    sprintf "{\"index\":%d,\"char\":\"%s\",\"x\":%d,\"y\":%d}" lp.Index lp.Char lp.X lp.Y

                sendFn json)
            (fun _ -> ())
            (fun () -> ())

    mouseSender, handle
