namespace PlayUno

module Cards =

    type Digit =
        private Digit of int with
        override this.ToString() =
            match this with
            | Digit d -> string d

    let digit d : Digit =
        if d < 0 || d > 9
        then invalidArg "d" "valid digits are between 0 and 9"
        else Digit d

    type Color = 
        | Red
        | Green
        | Blue
        | Yellow

    type Card =
        | Card     of Value : Digit *  Color : Color
        | KickBack of Color : Color
        | Skip     of Color : Color
        override this.ToString() =
            match this with
            | Card(n,c)  -> sprintf "%A %A" c n
            | KickBack c -> sprintf "%A kickback" c
            | Skip c     -> sprintf "%A skip" c

    let card (d : int, c : Color) =
        Card (digit d, c)

    let kickBack (c : Color) =
        KickBack c

    let skip (c : Color) =
        Skip c

    let color =
        function
        | Card (_,c) -> c
        | KickBack c -> c
        | Skip c     -> c

    let value =
        function
        | Card (v,_) -> Some v
        | _          -> None

    let isValidNextCard (onTop : Card) (turn : Card) =
        let validColor =
            color onTop = color turn
        let validValue =
            match (value onTop, value turn) with
            | Some (Digit top), Some (Digit t) -> top = t
            | _                                -> false
        validColor || validValue

    let isInvalidNextCard (onTop : Card) (turn : Card) =
        not <| isValidNextCard onTop turn