program casestmt(output);
var i: integer; c: char;
begin
   for i := 1 to 6 do
      case i of
         1: writeln('one');
         2, 3: writeln('two or three');
         4: ;
         5, 6: begin write('five'); writeln(' or six') end;
      end;
   for c := 'a' to 'd' do
      case c of
         'a', 'c': write('vowel? ');
         'b', 'd': case c of 'b': write('bee '); 'd': write('dee ') end
      end;
   writeln;
   case 3 > 2 of true: writeln('yes'); false: writeln('no') end
end.
