{ Records with variant parts: geometric figures selected by a tag field,
  allocated with new(p, tag) and processed with case and with statements. }
program shapes(output);
const pi = 3.14159265358979;
type kind = (circle, rectangle, triangle);
     point = record x, y: real end;
     figure = record
                 name: alfa;
                 origin: point;
                 case k: kind of
                    circle:    (radius: real);
                    rectangle: (width, height: real);
                    triangle:  (a, b, c: real)
              end;
     figref = ^figure;
var figs: array[1..4] of figref;
    i: integer;
    total: real;

function area(var f: figure): real;
var s: real;
begin
   with f do
      case k of
         circle:    area := pi * sqr(radius);
         rectangle: area := width * height;
         triangle:  begin
                       s := (a + b + c) / 2;
                       area := sqrt(s * (s - a) * (s - b) * (s - c))
                    end
      end
end;

function perimeter(var f: figure): real;
begin
   with f do
      case k of
         circle:    perimeter := 2 * pi * radius;
         rectangle: perimeter := 2 * (width + height);
         triangle:  perimeter := a + b + c
      end
end;

begin
   new(figs[1], circle);
   with figs[1]^ do begin name := 'wheel     '; origin.x := 0; origin.y := 0; radius := 1.5 end;
   new(figs[2], rectangle);
   with figs[2]^ do begin name := 'door      '; origin.x := 2; origin.y := 0; width := 0.9; height := 2.1 end;
   new(figs[3], triangle);
   with figs[3]^ do begin name := 'sail      '; origin.x := 5; origin.y := 1; a := 3; b := 4; c := 5 end;
   new(figs[4], rectangle);
   with figs[4]^, origin do begin name := 'table     '; x := -1; y := 3; width := 1.6; height := 0.8 end;
   total := 0;
   writeln('name        kind          x     y     area  perimeter');
   for i := 1 to 4 do
      with figs[i]^ do
      begin
         write(name, '  ');
         case k of
            circle:    write('circle    ');
            rectangle: write('rectangle ');
            triangle:  write('triangle  ')
         end;
         writeln(origin.x:5:1, origin.y:6:1, area(figs[i]^):9:3, perimeter(figs[i]^):11:3);
         total := total + area(figs[i]^)
      end;
   writeln('total area: ', total:8:3);
   { a variable allocated by new(p, t) is released by dispose(p, t), with the same tag value }
   for i := 1 to 4 do
      case figs[i]^.k of
         circle:    dispose(figs[i], circle);
         rectangle: dispose(figs[i], rectangle);
         triangle:  dispose(figs[i], triangle)
      end
end.
