module Program

open Fable.Core
open Fable.Python.TkInter
open Fable.Actor.Types
open Fable.Actor

// ---------------------------------------------------------------------------
// Factor Platform bootstrap
// ---------------------------------------------------------------------------

[<Erase>]
type IFactorPlatformInit =
    abstract ensureMainProcess: unit -> obj
    abstract processTimers: ms: int -> unit

[<ImportAll("factor_platform")>]
let factorPlatformInit: IFactorPlatformInit = nativeOnly

// ---------------------------------------------------------------------------
// Extensions for missing TkInter bindings
// ---------------------------------------------------------------------------

[<Emit("$0.geometry($1)")>]
let setGeometry (root: Tk) (size: string) : unit = nativeOnly

[<Emit("$0.configure(bg=$1)")>]
let setBg (root: Tk) (color: string) : unit = nativeOnly

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

let text = "TIME FLIES LIKE AN ARROW"

type MousePos = { X: int; Y: int }

type LetterMsg =
    | MouseMove of MousePos
    | Delayed of MousePos

// ---------------------------------------------------------------------------
// Actor-based pipeline
// ---------------------------------------------------------------------------

let setupPipeline (root: Tk) =
    let frame = Frame(root, 800, 600, "#1a1a2e")
    frame.pack ()

    let labels =
        text
        |> Seq.toList
        |> List.mapi (fun i c ->
            let label = Label(frame, string c, "#00ffff", "#1a1a2e")
            (i, label))

    // Spawn a letter actor for each character
    let letters =
        labels
        |> List.map (fun (index, label) ->
            spawn (fun () ->
                let me = self<LetterMsg> ()

                let rec loop () =
                    actor {
                        let! msg = receive<LetterMsg> ()

                        match msg with
                        | MouseMove pos ->
                            schedule (80 * index) (fun () -> send me (Delayed pos)) |> ignore
                            return! loop ()
                        | Delayed pos ->
                            label.place (pos.X + index * 12 + 15, pos.Y)
                            return! loop ()
                    }

                loop ()))

    // Distributor: receives MousePos, fans out to all letter actors
    let distributor: Actor<MousePos> =
        spawn (fun () ->
            let rec loop () =
                actor {
                    let! pos = receive<MousePos> ()
                    letters |> List.iter (fun letter -> send letter (MouseMove pos))
                    return! loop ()
                }

            loop ())

    distributor, frame

// ---------------------------------------------------------------------------
// Main
// ---------------------------------------------------------------------------

let main () =
    factorPlatformInit.ensureMainProcess () |> ignore
    printfn "Factor platform initialized"

    let root = Tk()
    root.title "Fable.Actor Timeflies - Python"
    setGeometry root "800x600"
    setBg root "#1a1a2e"

    let distributor, frame = setupPipeline root

    frame.bind (
        "<Motion>",
        fun (ev: Event) ->
            send distributor { X = ev.x; Y = ev.y }
    )
    |> ignore

    printfn "mouse bound, starting mainloop"

    let rec pump () =
        factorPlatformInit.processTimers 1
        root.after (16, pump)

    pump ()
    root.mainloop ()

main ()
