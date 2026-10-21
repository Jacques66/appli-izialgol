program varparams(output);
type small = 1..10;
var i: integer; s: small; p: packed array[1..3] of integer; r: real;
    rec: record case tag: boolean of true: (x: integer); false: () end;
procedure q(var x: integer); begin end;
begin
   q(i + 1);
   q(5);
   q(s);
   q(p[1]);
   q(r);
   q(rec.tag)
end.
