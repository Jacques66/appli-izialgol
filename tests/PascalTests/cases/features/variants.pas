program variants(output);
type kind = (int, re, ch, none);
     value = record
                case k: kind of
                   int: (i: integer);
                   re: (r: real);
                   ch: (c: char; code: integer);
                   none: ()
             end;
     shape = (sq, rc);
     nested = record
                id: integer;
                case s: shape of
                   sq: (side: integer);
                   rc: (w: integer;
                        case filled: boolean of
                           true: (color: char);
                           false: ())
              end;
     free = record case boolean of true: (a: integer); false: (b: real) end;
var v: array[1..4] of value; n: nested; i: integer; f: free;
procedure show(var v: value);
begin
   case v.k of
      int: writeln('integer ', v.i:1);
      re: writeln('real ', v.r:4:1);
      ch: writeln('char ', v.c, ' ', v.code:1);
      none: writeln('nothing')
   end
end;
begin
   v[1].k := int; v[1].i := 42;
   v[2].k := re; v[2].r := 2.5;
   v[3].k := ch; v[3].c := 'q'; v[3].code := ord('q');
   v[4].k := none;
   for i := 1 to 4 do show(v[i]);
   n.id := 1; n.s := rc; n.w := 3; n.filled := true; n.color := 'r';
   writeln(n.id:1, ' ', n.w:1, ' ', n.color);
   n.s := sq; n.side := 7; writeln(n.side:1);
   f.a := 10; writeln(f.a:1); f.b := 1.5; writeln(f.b:4:1)
end.
