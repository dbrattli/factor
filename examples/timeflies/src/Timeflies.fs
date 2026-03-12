module Timeflies

open Fable.Actor.Types
open Fable.Actor

let text = "TIME FLIES LIKE AN ARROW WITH F# AND FABLE.BEAM"

type MousePos = { X: int; Y: int }

type LetterMsg =
    | MouseMove of MousePos
    | Delayed of MousePos

/// Sets up the timeflies demo using actors.
///
/// Each letter is an actor with its own delay timer.
/// A distributor actor fans out mouse events to all letter actors.
/// Returns the distributor actor — send MousePos to it.
/// Kill the distributor to clean up (linked children die automatically).
let setupPipeline (sendFn: string -> unit) : Actor<MousePos> =
    spawn (fun () ->
        // Each letter is a linked child actor with a delay
        let letters =
            text
            |> Seq.toList
            |> List.mapi (fun index char ->
                spawnLinked (fun () ->
                    let me = self<LetterMsg> ()

                    let rec loop () =
                        actor {
                            let! msg = receive<LetterMsg> ()

                            match msg with
                            | MouseMove pos ->
                                schedule (80 * index) (fun () -> send me (Delayed pos)) |> ignore
                                return! loop ()
                            | Delayed pos ->
                                let json =
                                    sprintf
                                        "{\"index\":%d,\"char\":\"%s\",\"x\":%d,\"y\":%d}"
                                        index
                                        (string char)
                                        (pos.X + index * 14 + 15)
                                        pos.Y

                                sendFn json
                                return! loop ()
                        }

                    loop ()))

        // Distributor loop: receive MousePos, fan out to all letters
        let rec loop () =
            actor {
                let! pos = receive<MousePos> ()
                letters |> List.iter (fun letter -> send letter (MouseMove pos))
                return! loop ()
            }

        loop ())
