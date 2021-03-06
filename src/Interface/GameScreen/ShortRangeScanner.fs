module Interface.GameScreen.ShortRangeScanner

open Fable.React
open Fable.React.Props
open Game.Types
open Game.Utils.Position
open Game.Rules.Movement
open Game.Rules.Weapons
open Units.Vector
open Types
open Interface.Common

module Menu = 
  let items player optionalGameObject position =
    match optionalGameObject with
    | Some gameObject ->
      match gameObject.Attributes with
      | EnemyAttributes enemy ->
        match player.Targets |> Seq.tryFind (fun p -> p = position) with
        | Some _ -> [|("Remove target", position |> RemoveTarget) |> MenuItem|]
        | None -> 
          if canAddTarget player gameObject then
            [|("Add target", position |> AddTarget) |> MenuItem|]
          else
            [|"Cannot add more targets" |> NoActionLabel|]
      | StarbaseAttributes _ ->
        match player.Position.GalacticPosition = position.GalacticPosition, isAdjacent player.Position.SectorPosition position.SectorPosition, Option.isNone player.DockedWith with
        | true, true, true -> [|("Dock", position |> Dock) |> MenuItem|]
        | true, true, false -> [|("Dock", Undock) |> MenuItem|]
        | _ -> [|"Move next to the starbase to dock" |> NoActionLabel|]
      | StarAttributes  -> [|"A star. A really big star." |> NoActionLabel|] 
    | None ->
      if player.Position = position then
        [|"USS Discovery" |> NoActionLabel|]
      elif canMove player position then
        [|("Move to", position |> ImpulseTo) |> MenuItem|]
      else
        [|"Insufficient energy to move here" |> NoActionLabel |]

  let view menuOptions gameDispatch =
    div [Class "menu menuCentered"] [
      div [Class "menuArrowUpContainer"] [
        div [Class "menuArrowUp"] []
      ]
      div [Class "menuBody"] (
        menuOptions.MenuItems |>
        Seq.map (fun mi ->
          match mi with
          | NoActionLabel text -> label text
          | MenuItem (text, action) -> button [OnClick (fun _ -> action |> UpdateGameState |> gameDispatch)] [label text]
        )
      )
    ]

let phaserOverlay = FunctionComponent.Of(fun (props:{| player:Player ; gridWidthPercentage:float ; gridHeightPercentage:float ; optionalTarget: Game.Types.GameWorldPosition option |}) ->
  let player = props.player
  let gridWidthPercentage = props.gridWidthPercentage
  let gridHeightPercentage = props.gridHeightPercentage

  let size = Hooks.useState(0,0)
  let cachedTarget = Hooks.useState({X = -1<coordinatecomponent> ; Y = -1<coordinatecomponent>})
  let opacity,currentTarget =
    match props.optionalTarget with
    | Some target ->      
      1., if (cachedTarget.current <> target.SectorPosition) then cachedTarget.update(target.SectorPosition) ; target.SectorPosition else target.SectorPosition
    | None -> 0.,cachedTarget.current
  
  let container = Hooks.useRef None
  Interface.Browser.Helpers.debouncedSize container size.update
  
  let fromX = (((player.Position.SectorPosition.X |> float) * gridWidthPercentage ) + gridWidthPercentage/2.) * (size.current |> fst |> float)
  let fromY = (((player.Position.SectorPosition.Y |> float) * gridHeightPercentage ) + gridHeightPercentage/2.) * (size.current |> snd |> float)
  let toX = (((currentTarget.X |> float) * gridWidthPercentage ) + gridWidthPercentage/2.) * (size.current |> fst |> float)
  let toY = (((currentTarget.Y |> float) * gridHeightPercentage ) + gridHeightPercentage/2.) * (size.current |> snd |> float)
  let linePath = sprintf "M %f %f L %f %f" fromX fromY toX toY
  div [Class "phaserOverlay"; RefHook container ; Style [Opacity opacity]] [
    svg [
      Style [Width (sprintf "%dpx" (size.current |> fst)) ; Height (sprintf "%dpx" (size.current |> snd))]
      ViewBox (sprintf "0 0 %d %d" (size.current |> fst) (size.current |> snd))] [
      path [D linePath ; SVGAttr.Stroke "orange" ; SVGAttr.StrokeWidth 3.] []      
    ]
  ]
)

let view isUiDisabled (explosions:Explosion list) (gameObjects:GameObject array) (player:Player) (menuItems:ShortRangeScannerMenu option) optionalPhaserTarget dispatch gameDispatch =
  let gridWidthPercentage = 1. / ((GameWorldPosition.Max.SectorPosition.Y+1<coordinatecomponent>) |> float)
  let gridWidthPercentageAsString = sprintf "%f%%" (gridWidthPercentage * 100.)
  let gridHeightPercentage = 1. / ((GameWorldPosition.Max.SectorPosition.X+1<coordinatecomponent>) |> float)
  let gridHeightPercentageAsString = sprintf "%f%%" (gridHeightPercentage * 100.)
  let numberOfRows = ((GameWorldPosition.Max.SectorPosition.Y+1<coordinatecomponent>) |> int)
  let numberOfColumns = ((GameWorldPosition.Max.SectorPosition.X+1<coordinatecomponent>) |> int)
  let getLeft x = CSSProp.Left (sprintf "%f%%" ((x |> float) / ((GameWorldPosition.Max.SectorPosition.X+1<coordinatecomponent>) |> float) * 100.))
  let getTop y = CSSProp.Top (sprintf "%f%%" ((y |> float) / ((GameWorldPosition.Max.SectorPosition.Y+1<coordinatecomponent>) |> float) * 100.))
  let cssWidth = CSSProp.Width gridWidthPercentageAsString
  let cssHeight = CSSProp.Height gridHeightPercentageAsString

  let renderedSectorObjects = 
    [
      div [Class "gameObjects"] (
        gameObjects
        |> Seq.map (fun go ->
          div [Class "gameObject" ; Style [getLeft go.Position.SectorPosition.X ; getTop go.Position.SectorPosition.Y ; cssWidth ; cssHeight ]] [
            div [Style [Height "80%" ; Width "80%"]] [go |> renderGameObject]
          ]
        )
        |> Seq.append [
          div [Class "gameObject" ; Style [
            Transition (sprintf "left %s, top %s" Interface.Animation.scannerAnimationDurationCss Interface.Animation.scannerAnimationDurationCss) 
            getLeft player.Position.SectorPosition.X
            getTop player.Position.SectorPosition.Y
            cssWidth
            cssHeight ]
            ] [
            div [Style [Height "80%" ; Width "80%"]] [renderPlayer ()]
          ]
        ]
      )
    ]    

  let renderedExplosions =
    explosions
    |> Seq.map (fun explosion ->
      match explosion with
      | ExplodingEnemyScout position ->
        div [Class "explosion" ; Style [getLeft position.SectorPosition.X ; getTop position.SectorPosition.Y ; cssWidth ; cssHeight ]] [
          div [Style [Height "80%" ; Width "80%"]] [Units.Pixelated.Scout.pixelatedScout ({| dispatch = (fun _ -> ()) |})]
        ]
    )

  
  
  let overlayGrid =
    let gridTemplateRows = (Seq.replicate numberOfRows (sprintf "%s " gridHeightPercentageAsString)) |> Seq.toArray |> Array.fold (+) ""
    let gridTemplateColumns = (Seq.replicate numberOfColumns (sprintf "%s " gridWidthPercentageAsString)) |> Seq.toArray |> Array.fold (+) ""
    div [Class "overlayGrid" ; Style [CSSProp.GridTemplateRows gridTemplateRows ; CSSProp.GridTemplateColumns gridTemplateColumns ]] (
      Game.Utils.Position.sectorCoordinateIterator ()
      |> Seq.map(fun xyPosition ->
        let gameWorldPosition = { player.Position with SectorPosition = xyPosition }
        div [
          OnClick (fun _ ->
            if isUiDisabled then
              ()
            else
              match menuItems with
              | Some _ -> ()
              | None ->            
                let objectAtPosition = gameObjects |> Seq.tryFind(fun go -> go.Position = gameWorldPosition)
                let menuItems = Menu.items player objectAtPosition gameWorldPosition
                if menuItems |> Seq.isEmpty then () else (gameWorldPosition, menuItems) |> ShowShortRangeScannerMenu |> dispatch
          )
          Style [
            Position PositionOptions.Relative
            GridRowStart ((xyPosition.Y|>int) + 1)
            GridRowEnd ((xyPosition.Y|>int) + 2)
            GridColumnStart ((xyPosition.X|>int) + 1)
            GridColumnEnd ((xyPosition.X|>int) + 2)
          ]
        ] [
          match menuItems with
          | Some menu ->
            if menu.Position = gameWorldPosition then 
              Menu.view menu gameDispatch
            else
              fragment [] []
          | None -> fragment [] []
        ]
      )
    )

  let verticalLines =
    { 0..(numberOfColumns-2) }
    |> Seq.map(fun g ->
      let leftPercentage = sprintf "%f%%" ((g |> float) / ((GameWorldPosition.Max.SectorPosition.X+1<coordinatecomponent>) |> float) * 100.)
      div [Class "verticalLine" ; Style [CSSProp.Left leftPercentage ; cssWidth]] []
    )
  let horizontalLines =
    { 0..(numberOfRows-2) }
    |> Seq.map(fun g ->
      let topPercentage = sprintf "%f%%" ((g |> float) / ((GameWorldPosition.Max.SectorPosition.Y+1<coordinatecomponent>) |> float) * 100.)
      div [Class "horizontalLine" ; Style [CSSProp.Top topPercentage ; cssHeight]] []
    )    
  
  let phasers =
    phaserOverlay({| player=player ; gridWidthPercentage=gridWidthPercentage ; gridHeightPercentage=gridHeightPercentage ; optionalTarget = optionalPhaserTarget |})    

  div [Class "shortRangeScanner"] (
    [overlayGrid]
    |> Seq.append [phasers]
    |> Seq.append renderedSectorObjects 
    |> Seq.append renderedExplosions
    |> Seq.append verticalLines
    |> Seq.append horizontalLines    
  )