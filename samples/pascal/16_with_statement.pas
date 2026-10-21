{ The with statement: nested records (dates inside a record of persons),
  several records in one with statement, and complex arithmetic on records. }
program withdemo(output);
type month = (jan, feb, mar, apr, may, jun, jul, aug, sep, oct, nov, dec);
     date = record
               day: 1..31;
               mon: month;
               year: integer
            end;
     person = record
                 name: alfa;
                 birth: date
              end;
     complex = record re, im: real end;
var p: array[1..3] of person;
    i: integer;
    z, w, prod: complex;

procedure writemonth(m: month);
const names = 'JanFebMarAprMayJunJulAugSepOctNovDec';
type str36 = packed array[1..36] of char;
var s: str36;
    k: integer;
begin
   s := names;
   for k := 1 to 3 do write(s[3 * ord(m) + k])
end;

begin
   with p[1] do begin name := 'Wirth     '; with birth do begin day := 15; mon := feb; year := 1934 end end;
   with p[2], birth do begin name := 'Dijkstra  '; day := 11; mon := may; year := 1930 end;
   with p[3], birth do begin name := 'Hoare     '; day := 11; mon := jan; year := 1934 end;
   for i := 1 to 3 do
      with p[i], birth do
      begin
         write(name, ' born ', day:2, ' ');
         writemonth(mon);
         writeln(' ', year:4)
      end;
   z.re := 1; z.im := 2;
   w.re := 3; w.im := -1;
   with prod do
   begin
      re := z.re * w.re - z.im * w.im;
      im := z.re * w.im + z.im * w.re;
      writeln('(1+2i)(3-i) = ', re:1:1, ' + ', im:1:1, 'i')
   end
end.
