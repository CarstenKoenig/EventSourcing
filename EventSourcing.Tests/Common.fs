namespace EventSourcing.Tests

[<AutoOpen>]
module Common =

    let ignoreExceptions exVal f =
        try
            f ()
        with
        | _ -> exVal

