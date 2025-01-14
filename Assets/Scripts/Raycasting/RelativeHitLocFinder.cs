using System.IO.Pipes;
using System.Collections.Specialized;
using System.Numerics;
using System.Collections;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using Vector3 = UnityEngine.Vector3;
using Vector2 = UnityEngine.Vector2;

namespace VirtualMaze.Assets.Scripts.Raycasting
{
   
    public class RelativeHitLocFinder {
        

        /// <summary>
        /// Finds relative hit given a raycastHit on an object. Takes reference from center point of the object's master, and takes into account normal.
        /// Convention is for positive y in the relative hit to be going up, and cross product of normal and positive y vector is positive x. 
        /// </summary>
        /// <param name="raycastHit"></param> the RaycastHit object to find relative hit for
        /// <returns></returns> the 2d vector of the relative hit projected onto hit surface
        public static Vector2 getRelativeHit(RaycastHit raycastHit){
            Vector3 normal = raycastHit.normal;
            
            if (Math.Abs(normal.x) <= 0.001 && Math.Abs(normal.z) <= 0.001) {
                return getRelativeHitForVertical(raycastHit); //handle special case where it's just y.
            }
            
            GameObject gameObjectHit = raycastHit.transform.gameObject;
            Vector3 hitLoc = raycastHit.point;
            // calculate relative on surface
            GameObject objectMaster = getMaster(gameObjectHit);
            Vector3 relative3D = hitLoc - objectMaster.transform.position;
            Vector3 projection = Vector3.Dot(relative3D,normal)  * normal;
            // normal is a unit vector, and this is a standard formula (https://en.wikipedia.org/wiki/Vector_projection)
            Vector3 rejection = relative3D - projection;
            // rejection is the sum of x and y relatives (in 2d form), 
            // it's the "hypotenuse" if you imagine the hitpoint being projected onto the 2d plane of obj surface
            // I.e. it's the encoding of (relativeX, relativeY) but in a 3-d vector in absolute coordinates
            // our aim here is to somehow project this 3-d vector into the 2-d surface plane.

            // cross of (normal,y) gives relative x-vector
            Vector3 relativeXUnitVector = Vector3.Cross(normal,Vector3.up).normalized;
            // subtraction of rejection noramlised and relativeX unit vector must give relative Y unit vector
            Vector3 relativeYUnitVector = (rejection.normalized - relativeXUnitVector).normalized;

            float relativeX = Vector3.Dot(rejection, relativeXUnitVector); // project the rejection onto x-unit to get x
            float relativeY = Vector3.Dot(rejection, relativeYUnitVector); // project rejection onto y-unit to get y
        
            return new Vector2(relativeX, relativeY);
            
        }


        private static Vector2 getRelativeHitForVertical(RaycastHit raycastHit) {
            // figure thit out
            // we are assured that it's a vertical surface
            // On ground, +x then +z
            // On ceiling, same
            Vector3 relativePoint = raycastHit.point - getMaster(raycastHit.transform.gameObject).transform.position;
            return new Vector2(relativePoint.x, relativePoint.z);
        }
        

        /// <summary>
        /// Gets chained name which includes full tree hierachy of the object's name
        /// </summary>
        /// <param name="gameObject"></param> the object to find hierachy for
        /// <returns></returns> the full chained name
        public static String getChainedName(GameObject gameObject) {

            String chainedName = gameObject.name;   
            GameObject curObject = gameObject;
            while ((curObject.transform.parent != null) && (curObject.tag != "ObjectMaster")) {
                // implicitly, if the parent is null, we've hit the top of the tree and must return
                Poster posterScript; //Poster is a script, that is attached to poster objects.
                if  (curObject.TryGetComponent<Poster>(out posterScript)) {
                    curObject = posterScript.AttachedTo;
                } else {
                    curObject = curObject.transform.parent.gameObject; //wow
                }
                chainedName += "_" + curObject.name;
            }
            return chainedName;
        }

        private static GameObject getMaster(GameObject gameObject) {
            //Debug.Log("Entered getMaster");
            if (gameObject == null || gameObject.transform == null) {
                return gameObject;
            }
            GameObject curObject = gameObject;

            
            while ((curObject.transform.parent != null) && (curObject.tag != "ObjectMaster")) {
                // implicitly, if the parent is null, we've hit the top of the tree and must return
                //Debug.Log(curObject.name);
                Poster posterScript; //Poster is a script, that is attached to poster objects.
                if  (curObject.TryGetComponent<Poster>(out posterScript)) {

                    curObject = posterScript.AttachedTo;
                } else {
                    curObject = curObject.transform.parent.gameObject; //wow
                }

            }
            //Debug.Log("Exited getMaster");
            return curObject;
        }

    }

}