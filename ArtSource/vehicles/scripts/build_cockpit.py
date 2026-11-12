import bpy,math
from mathutils import Vector,Matrix
from math import sin,cos,pi
globals().update(bpy.app.driver_namespace['INT_BUILD'])
# Correct module transforms before placing the seating instances.
for o in list(group('04 Sellerie').objects):bpy.data.objects.remove(o,do_unlink=True)
src=open(r'D:\Docs\s&box projects\void_breach\ArtSource\vehicles\scripts\rebuild_interior.py',encoding='utf-8').read()
exec(src[src.index('def seat('):src.index('# Vertical rails')])

g='07 Cabine'
box('Estrade conducteur',(2.9,.57,.46),(1.29,.88,.18),floor,g,.025)
box('Panneau sous pare-brise',(3.50,0,.85),(.08,1.97,.60),ivory,g,.018)
box('Planche de bord principale',(3.29,.53,1.065),(.40,.91,.16),dark,g,.055)
box('Retour de planche de bord passager',(3.48,-.50,1.08),(.20,.94,.095),dark,g,.03)
box('Console latérale conducteur',(2.89,.96,.89),(.97,.14,.47),ivory,g,.035)
box('Plateau console latérale',(2.89,.95,1.133),(.97,.16,.055),dark,g,.012)
box('Socle siège suspendu',(2.60,.58,.67),(.34,.36,.26),dark,g,.035)
for z in [.58,.615,.65,.685,.72]:box('Soufflet de suspension',(2.60,.58,z),(.36,.38,.025),rubber,g,.009)
box('Glissière siège conducteur',(2.60,.58,.79),(.43,.39,.045),silver,g,.007)
box('Assise conducteur',(2.62,.58,.865),(.47,.43,.13),dark,g,.045)
o=box('Dossier conducteur',(2.405,.58,1.17),(.13,.43,.61),dark,g,.052);o.rotation_euler.y=-.1
box('Appuie-tête conducteur',(2.37,.58,1.525),(.135,.28,.17),dark,g,.043)
for y in [.36,.8]:
    rod('Support accoudoir',(2.46,y,1.02),(2.46,y,1.13),.014,silver,g)
    box('Accoudoir conducteur',(2.58,y,1.145),(.31,.065,.065),dark,g,.027)
for x in [2.46,2.55,2.64,2.73]:box('Piqûre assise',(x,.58,.931),(.002,.31,.001),rubber,g,0)
for y in [.49,.69]:
    o=box('Pédale frein' if y>.6 else 'Pédale accélérateur',(3.18,y,.595),(.17,.088,.038),rubber,g,.009);o.rotation_euler.y=-.35
    for x in [3.12,3.15,3.18,3.21,3.24]:box('Strie de pédale',(x,y,.622+(3.18-x)*.35),(.008,.077,.009),dark,g,.002)
rod('Colonne de direction',(3.22,.582,.59),(2.986,.582,1.233),.055,dark,g)
rod('Soufflet colonne',(3.18,.582,.62),(3.13,.582,.77),.075,rubber,g)

# Renault two-spoke/fork wheel: broad center pad and three lower apertures.
gw='08 Volant Renault'
C=Vector((2.9856169,.5820701,1.233369));U=Vector((0,-1,0));V=Vector((.855,0,.519)).normalized();N=U.cross(V).normalized()
def wp(u,v,n=0):return tuple(C+u*U+v*V+n*N)
wheelpts=[wp(.216*cos(a),.216*sin(a)) for a in [i*2*pi/96 for i in range(97)]]
tube('Volant | jante continue 460 mm',wheelpts,.0165,dark,gw,12)
# More subtle molded seam on the back of the rim.
tube('Volant | joint de moulage',[wp(.217*cos(a),.217*sin(a),-.014) for a in [i*2*pi/96 for i in range(97)]],.0012,rubber,gw,6)
def wheelpoly(name,points,depth,offset=0):
    vv=[wp(u,v,offset+n) for n in [-depth/2,depth/2] for u,v in points];k=len(points)
    ff=[tuple(reversed(range(k))),tuple(range(k,k*2))]+[(i,(i+1)%k,(i+1)%k+k,i+k) for i in range(k)]
    return mesh(name,vv,ff,dark,gw,.006)
wheelpoly('Volant | coussin central trapézoïdal',[(-.065,.095),(.065,.095),(.072,-.091),(.056,-.117),(-.056,-.117),(-.072,-.091)],.035,.012)
wheelpoly('Volant | branche gauche',[(-.060,-.030),(-.187,-.095),(-.171,-.139),(-.058,-.092)],.024)
wheelpoly('Volant | branche droite',[(.060,-.030),(.187,-.095),(.171,-.139),(.058,-.092)],.024)
for s in [-1,1]:wheelpoly('Volant | fourche inférieure',[(s*.063,-.088),(s*.076,-.200),(s*.037,-.214),(s*.030,-.105)],.025)
R=Matrix(((U.x,V.x,N.x),(U.y,V.y,N.y),(U.z,V.z,N.z))).to_4x4()
o=box('Volant | cartouche Renault',wp(0,-.073,.034),(.103,.018,.003),silver,gw,.002)
o.rotation_euler=R.to_euler()
o=text('Volant | RENAULT','RENAULT',wp(0,-.078,.036),.012,rubber,gw);o.rotation_euler=R.to_euler()
rod('Commodo clignotants',wp(-.04,.025,-.08),wp(-.255,.02,-.08),.007,dark,g)
rod('Commodo essuie-glaces',wp(.04,.01,-.08),wp(.25,.04,-.08),.007,dark,g)

# Instrument fascia in its own inclined plane: analog speedometer, tachograph, telltales.
gi='09 Instruments'
P=Vector((3.265,.57,1.30));A=Vector((0,-1,0));B=Vector((.30,0,.954)).normalized();D=A.cross(B).normalized()
Q=Matrix(((A.x,B.x,D.x),(A.y,B.y,D.y),(A.z,B.z,D.z))).to_4x4()
def pp(x,y,z=0):return tuple(P+x*A+y*B+z*D)
def panelbox(name,x,y,w,h,d,mat,z=0):
    o=box(name,pp(x,y,z),(w,h,d),mat,gi,min(.008,w*.12,h*.12));o.rotation_euler=Q.to_euler();return o
def label(body,x,y,size=.009,mat=white,z=.032):
    o=text('Sérigraphie '+body,body,pp(x,y,z),size,mat,gi);o.rotation_euler=Q.to_euler();return o
panelbox('Combiné instruments | coque',0,0,.50,.255,.10,dark,-.03)
panelbox('Casquette anti-reflet',0,.141,.54,.031,.16,dark,-.02)
panelbox('Tachographe | cerclage',-.13,.035,.166,.11,.017,silver,.023)
panelbox('Tachographe | façade',-.13,.035,.152,.096,.021,rubber,.03)
panelbox('Tachographe | LCD',-.13,.057,.116,.025,.005,ivory,.044)
label('08:45   002738',-.13,.052,.009,dark,.048)
label('KIENZLE',-.13,.014,.009,white,.046)
for x in [-.18,-.147,-.114,-.081]:panelbox('Touche tachographe',x,-.003,.019,.009,.006,dark,.045)
def dial(x,y,r,kind):
    pts=[pp(x+r*cos(a),y+r*sin(a),.028) for a in [i*2*pi/64 for i in range(65)]]
    tube('Cadran | cerclage',pts,.0055,silver,gi)
    vs=[pp(x,y,.026)]+[pp(x+r*cos(i*2*pi/64),y+r*sin(i*2*pi/64),.026) for i in range(64)]
    mesh('Cadran | fond noir',vs,[(0,i+1,(i+1)%64+1) for i in range(64)],rubber,gi)
    for i in range(33):
        a=math.radians(225-i*270/32);r1=r*(.76 if i%4==0 else .86)
        rod('Graduation',pp(x+r1*cos(a),y+r1*sin(a),.033),pp(x+r*.94*cos(a),y+r*.94*sin(a),.033),.0009,white,gi)
    for i in range(9):
        a=math.radians(225-i*270/8)
        label(str(i*20),x+r*.64*cos(a),y+r*.64*sin(a)-.004,.008,white,.035)
    rod('Aiguille orange',pp(x,y,.037),pp(x-r*.54,y-r*.54,.037),.0017,red,gi)
    label(kind,x,y-.025,.007,white,.036)
    panelbox('Compteur kilométrique',x,y-.044,.052,.011,.003,dark,.033)
    label('027386',x,y-.047,.007,white,.037)
dial(.085,.022,.088,'km/h')
for i in range(7):
    panelbox('Voyant latéral',-.014,-.088+i*.029,.019,.018,.006,red if i%3==0 else dark,.032)
    label(['P','!','O','D','N','R','+'][i],-.014,-.091+i*.029,.008,white,.038)
panelbox('Platine portes',.335,-.045,.19,.225,.055,ivory,.001)
for row in range(3):
    for col in range(3):
        x=.275+col*.059;y=.021-row*.052
        panelbox('Interrupteur de porte',x,y,.049,.043,.014,rubber,.038)
        # Two door leaves and center split icon, actual geometry for legibility in game.
        for dx in [-.009,.009]:
            panelbox('Pictogramme vantail',x+dx,y,.010,.019,.001,white,.047)
label('PORTES',.335,-.143,.009,dark,.034)
for i in range(3):
    panelbox('Bouton alarme',.275+i*.059,.076,.05,.025,.014,red,.037)
    label(['!','AIR','STOP'][i],.275+i*.059,.073,.007,white,.046)
for i in range(6):
    panelbox('Voyant inférieur',-.19+i*.06,-.098,.04,.018,.008,red if i==4 else dark,.031)
    label(['BAT','HUILE','EAU','AIR','STOP','FEUX'][i],-.19+i*.06,-.101,.0048,white,.037)
for i in range(4):
    for j in range(3):
        box('Interrupteur console gauche',(2.63+i*.072,.916+j*.032,1.169),(.055,.024,.018),rubber,g,.004)
        box('Repère interrupteur',(2.63+i*.072,.916+j*.032,1.180),(.022,.003,.002),white,g,.001)
for x in [3.02,3.13]:
    rod('Bouton console',(x,.95,1.16),(x,.95,1.187),.017,red if x>3.1 else dark,g)
for y in [-.5,-.2,.2,.58,.85]:
    box('Déflecteur désembuage',(3.48,y,1.14),(.08,.22,.015),rubber,g,.006)
    for dy in [-.075,-.025,.025,.075]:box('Ailette désembuage',(3.48,y+dy,1.15),(.071,.008,.008),dark,g,.002)

# Driver partition and entry ticket machine.
glass=material('Verre cloison cabine',(.21,.29,.30),0,.08)
p=next(n for n in glass.node_tree.nodes if n.type=='BSDF_PRINCIPLED');p.inputs['Transmission Weight'].default_value=1;p.inputs['IOR'].default_value=1.45
box('Cloison conducteur | panneau bas',(2.30,.63,.84),(.065,.77,.92),ivory,g,.018)
box('Cloison conducteur | vitrage',(2.30,.64,1.64),(.012,.72,.66),glass,g,.025)
for y in [.265,1.015]:rod('Profil cloison',(2.30,y,.38),(2.30,y,2.02),.015,railmat,g)
rod('Traverse cloison',(2.30,.265,2.02),(2.30,1.015,2.02),.015,railmat,g)
box('Portillon cabine',(2.54,.18,.87),(.43,.038,.78),ivory,g,.03)
rod('Prise portillon',(2.36,.18,1.28),(2.74,.18,1.28),.016,railmat,g)
rod('Montant valideur',(2.51,-.43,.37),(2.51,-.43,1.49),.018,railmat,g)
box('Valideur | boîtier',(2.51,-.43,1.27),(.18,.19,.31),dark,g,.035)
box('Valideur | face bleue',(2.405,-.43,1.28),(.035,.168,.27),blue,g,.018)
box('Valideur | écran',(2.381,-.43,1.35),(.009,.12,.06),rubber,g,.004)
box('Valideur | fente',(2.377,-.43,1.245),(.008,.105,.009),silver,g,.001)
bpy.context.view_layer.update()
bpy.app.driver_namespace['INT_BUILD']=globals().copy()
print('Cockpit and reference wheel complete')
