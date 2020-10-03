module Interface.Common
open Fable.React
open Fable.React.Props

module GameColors =
  let danger = sprintf "rgba(230,3,3,%f)"
  let warning = sprintf "rgba(252,186,3,%f)"
  let healthy = sprintf "rgb(0,230,0,%f)"
  let indicatorBackgroundColor = "rgba(0,0,0,0.4)"

let labelAtRow row text =
  div [Class "label" ; Style [CSSProp.GridRow row]] [str text]

let label text =
  div [Class "label"] [str text]

let floatLabel floatValue =
  div [Class "label"] [str (sprintf "%.0f" floatValue)]

let arc x y radius startAngle endAngle attributes =
  let polarToCartesian centerX centerY radius angleInDegrees =
    let angleInRadians = (angleInDegrees-90.) * System.Math.PI / 180.
    (centerX + (radius * cos angleInRadians) , centerY + (radius * sin angleInRadians))

  let startX, startY = polarToCartesian x y radius endAngle
  let endX, endY = polarToCartesian x y radius startAngle
  let arcSweep = if (endAngle - startAngle) <= 180. then "0" else "1"

  let d =
    sprintf "M %f %f A %f %f 0 %s 0 %f %f "
      startX
      startY
      radius
      radius
      arcSweep
      endX
      endY
      
  path ([(SVGAttr.D d) :> IProp] |> Seq.append attributes) []